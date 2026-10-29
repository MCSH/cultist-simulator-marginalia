using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using SecretHistories.Entities;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.UI;

namespace MarginaliaMod
{
    // Guides drawn on the tabletop for laying cards out by hand: an n x m grid of
    // card-sized cells, or a horizontal / vertical line ticked every card width / height.
    // They never touch the cards. Rulers snap so their cells line up with where the game
    // snaps cards, sit beneath the board labels, and are global like the labels, kept in
    // marginalia_rulers.json beside the saves.
    public static class Rulers
    {
        public enum Shape
        {
            Grid,
            HorizontalLine,
            VerticalLine
        }

        public sealed class Data
        {
            public string Id;
            [JsonConverter(typeof(StringEnumConverter))]
            public Shape Shape;
            // Top-left corner, in tabletop-local units.
            public float X;
            public float Y;
            // A horizontal line's length is Columns, a vertical line's is Rows.
            public int Columns = 1;
            public int Rows = 1;
        }

        public const int MaxCells = 20;

        // DropzoneManifestation's card size; used until the table reports its grid.
        private static readonly Vector2 FallbackCell = new Vector2(75f, 115f);

        // How far either side of a line still counts as clicking it.
        private const float LineHitPadding = 18f;

        private static List<Data> _rulers = new List<Data>();
        private static readonly Dictionary<string, RulerGraphic> Views = new Dictionary<string, RulerGraphic>();
        private static RectTransform _root;
        private static float _nextSphereSearch;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "marginalia_rulers.json");

        public static void Load()
        {
            _rulers = JsonFile.Read(FilePath, new List<Data>());
            // Reloaded (with newer data from Steam Cloud): draw them again.
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root.gameObject);
                _root = null;
                _nextSphereSearch = 0f;
            }
        }

        public static void Save()
        {
            JsonFile.Write(FilePath, _rulers);
        }

        public static Data Get(string id) => _rulers.FirstOrDefault(r => r.Id == id);

        // Called every frame after BoardLabels.Tick, so the rulers' layer goes in just
        // beneath the labels' layer.
        public static void Tick()
        {
            if (_root != null || !BoardLabels.IsAttached || Time.unscaledTime < _nextSphereSearch)
                return;
            _nextSphereSearch = Time.unscaledTime + 1f;

            var sphere = UnityEngine.Object.FindObjectOfType<TabletopSphere>();
            if (sphere == null)
                return;

            Views.Clear();
            var sphereRect = sphere.GetRectTransform();
            _root = UiKit.Rect("MarginaliaRulers", sphereRect);
            _root.localPosition = Vector3.zero;
            _root.localScale = Vector3.one;
            _root.SetSiblingIndex(BoardLabels.SiblingIndexAboveBackground(sphereRect));

            var cell = CellSize();
            NoonUtility.Log($"Marginalia: table grid cell {cell.x} x {cell.y}, snap step x{SnapCoefficient()}");

            foreach (var ruler in _rulers)
                Spawn(ruler);
        }

        // The table's grid cell, which is one card.
        public static Vector2 CellSize()
        {
            var meniscate = Watchman.Get<Meniscate>();
            var cell = meniscate != null ? meniscate.GetGridCellSize() : Vector2.zero;
            return cell.x > 1f && cell.y > 1f ? cell : FallbackCell;
        }

        // Mirrors AbstractChoreographer.GetGridSnapCoefficient: cards snap to multiples
        // of the cell size times this, or not at all when it's 0.
        private static float SnapCoefficient()
        {
            try
            {
                var setting = Watchman.Get<Compendium>().GetEntityById<Setting>("GridSnapLevel");
                switch (Convert.ToInt32(Convert.ToSingle(setting.CurrentValue)))
                {
                    case 0: return 0f;
                    case 1: return 1f;
                    case 2: return 0.5f;
                    case 3: return 0.25f;
                    default: return 0.125f;
                }
            }
            catch (Exception)
            {
                return 0.5f;
            }
        }

        // Card centres snap to the grid, so snap the centre of the ruler's first cell.
        private static Vector2 SnapCorner(Vector2 corner)
        {
            var cell = CellSize();
            float coefficient = SnapCoefficient();
            if (coefficient <= 0f)
                return corner;
            var half = new Vector2(cell.x / 2f, -cell.y / 2f);
            var step = cell * coefficient;
            var centre = corner + half;
            centre = new Vector2(Mathf.Round(centre.x / step.x) * step.x, Mathf.Round(centre.y / step.y) * step.y);
            return centre - half;
        }

        public static string Describe(Data ruler)
        {
            switch (ruler.Shape)
            {
                case Shape.Grid: return $"Grid, {ruler.Columns} × {ruler.Rows}";
                case Shape.HorizontalLine: return $"Line, {ruler.Columns} card{(ruler.Columns == 1 ? "" : "s")} wide";
                default: return $"Line, {ruler.Rows} card{(ruler.Rows == 1 ? "" : "s")} tall";
            }
        }

        public static Data RulerAt(Vector2 screen)
        {
            if (!BoardLabels.ScreenToBoard(screen, out var point))
                return null;
            // Later rulers draw on top, so test them first.
            for (int i = _rulers.Count - 1; i >= 0; i--)
            {
                if (Bounds(_rulers[i]).Contains(point))
                    return _rulers[i];
            }
            return null;
        }

        private static Rect Bounds(Data ruler)
        {
            var cell = CellSize();
            switch (ruler.Shape)
            {
                case Shape.Grid:
                    return new Rect(ruler.X, ruler.Y - ruler.Rows * cell.y, ruler.Columns * cell.x, ruler.Rows * cell.y);
                case Shape.HorizontalLine:
                    return new Rect(ruler.X, ruler.Y - LineHitPadding, ruler.Columns * cell.x, LineHitPadding * 2f);
                default:
                    return new Rect(ruler.X - LineHitPadding, ruler.Y - ruler.Rows * cell.y, LineHitPadding * 2f, ruler.Rows * cell.y);
            }
        }

        // Adds a ruler with its first cell under the given point. Not saved until Save().
        public static Data Add(Shape shape, Vector2 position, int columns, int rows)
        {
            var cell = CellSize();
            var corner = SnapCorner(position - new Vector2(cell.x / 2f, -cell.y / 2f));
            var ruler = new Data { Id = Guid.NewGuid().ToString("N"), Shape = shape, X = corner.x, Y = corner.y };
            _rulers.Add(ruler);
            Spawn(ruler);
            Resize(ruler, columns, rows);
            return ruler;
        }

        public static void Resize(Data ruler, int columns, int rows)
        {
            ruler.Columns = Mathf.Clamp(columns, 1, MaxCells);
            ruler.Rows = Mathf.Clamp(rows, 1, MaxCells);
            Refresh(ruler);
        }

        public static Vector2 Corner(Data ruler) => new Vector2(ruler.X, ruler.Y);

        public static void Move(Data ruler, Vector2 corner)
        {
            corner = SnapCorner(corner);
            ruler.X = corner.x;
            ruler.Y = corner.y;
            Refresh(ruler);
        }

        public static void Remove(Data ruler)
        {
            _rulers.Remove(ruler);
            if (Views.TryGetValue(ruler.Id, out var view) && view != null)
                UnityEngine.Object.Destroy(view.gameObject);
            Views.Remove(ruler.Id);
            Save();
        }

        private static void Spawn(Data ruler)
        {
            if (_root == null)
                return;
            var rect = UiKit.Rect("Ruler", _root);
            rect.pivot = new Vector2(0f, 1f);
            var graphic = rect.gameObject.AddComponent<RulerGraphic>();
            graphic.raycastTarget = false;
            Views[ruler.Id] = graphic;
            Refresh(ruler);
        }

        private static void Refresh(Data ruler)
        {
            if (!Views.TryGetValue(ruler.Id, out var view) || view == null)
                return;
            view.Ruler = ruler;
            view.Cell = CellSize();
            view.rectTransform.localPosition = new Vector2(ruler.X, ruler.Y);
            view.SetVerticesDirty();
        }
    }

    // Draws one ruler as plain quads, in tabletop units with the origin at its top-left.
    internal class RulerGraphic : MaskableGraphic
    {
        private static readonly Color Fill = new Color(0.72f, 0.58f, 0.32f, 0.07f);
        private static readonly Color Edge = new Color(0.72f, 0.58f, 0.32f, 0.75f);
        private static readonly Color Divider = new Color(0.72f, 0.58f, 0.32f, 0.45f);

        private const float EdgeWidth = 3f;
        private const float DividerWidth = 2f;
        private const float Dash = 10f;
        private const float Gap = 8f;
        private const float Tick = 16f;
        private const float EndTick = 30f;

        public Rulers.Data Ruler;
        public Vector2 Cell;

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            if (Ruler == null)
                return;
            switch (Ruler.Shape)
            {
                case Rulers.Shape.Grid:
                    DrawGrid(vh, Ruler.Columns, Ruler.Rows);
                    break;
                case Rulers.Shape.HorizontalLine:
                    DrawLine(vh, Ruler.Columns, true);
                    break;
                default:
                    DrawLine(vh, Ruler.Rows, false);
                    break;
            }
        }

        private void DrawGrid(VertexHelper vh, int columns, int rows)
        {
            float width = columns * Cell.x;
            float height = rows * Cell.y;
            Quad(vh, 0f, -height, width, 0f, Fill);

            for (int c = 1; c < columns; c++)
                DashedVertical(vh, c * Cell.x, height);
            for (int r = 1; r < rows; r++)
                DashedHorizontal(vh, -r * Cell.y, width);

            float e = EdgeWidth / 2f;
            Quad(vh, -e, -e, width + e, e, Edge);
            Quad(vh, -e, -height - e, width + e, -height + e, Edge);
            Quad(vh, -e, -height + e, e, -e, Edge);
            Quad(vh, width - e, -height + e, width + e, -e, Edge);
        }

        private void DrawLine(VertexHelper vh, int cells, bool horizontal)
        {
            float step = horizontal ? Cell.x : Cell.y;
            float length = cells * step;
            float e = EdgeWidth / 2f;
            for (int i = 0; i <= cells; i++)
            {
                float half = (i == 0 || i == cells ? EndTick : Tick) / 2f;
                float at = i * step;
                if (horizontal)
                    Quad(vh, at - e, -half, at + e, half, Edge);
                else
                    Quad(vh, -half, -at - e, half, -at + e, Edge);
            }
            if (horizontal)
                Quad(vh, 0f, -e, length, e, Edge);
            else
                Quad(vh, -e, -length, e, 0f, Edge);
        }

        private static void DashedVertical(VertexHelper vh, float x, float height)
        {
            float w = DividerWidth / 2f;
            for (float y = Gap / 2f; y < height; y += Dash + Gap)
                Quad(vh, x - w, -Mathf.Min(y + Dash, height), x + w, -y, Divider);
        }

        private static void DashedHorizontal(VertexHelper vh, float y, float width)
        {
            float w = DividerWidth / 2f;
            for (float x = Gap / 2f; x < width; x += Dash + Gap)
                Quad(vh, x, y - w, Mathf.Min(x + Dash, width), y + w, Divider);
        }

        private static void Quad(VertexHelper vh, float x0, float y0, float x1, float y1, Color32 color)
        {
            int i = vh.currentVertCount;
            vh.AddVert(new Vector3(x0, y0), color, Vector2.zero);
            vh.AddVert(new Vector3(x0, y1), color, Vector2.zero);
            vh.AddVert(new Vector3(x1, y1), color, Vector2.zero);
            vh.AddVert(new Vector3(x1, y0), color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }
    }

    public static class RulerMenuItems
    {
        private const int DefaultColumns = 4;
        private const int DefaultRows = 3;
        private const int DefaultLength = 5;

        public static void Register()
        {
            Add("ruler.add-grid", 30, "Add grid…", Rulers.Shape.Grid);
            Add("ruler.add-hline", 31, "Add line (H)…", Rulers.Shape.HorizontalLine);
            Add("ruler.add-vline", 32, "Add line (V)…", Rulers.Shape.VerticalLine);

            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "ruler.resize",
                Order = -2,
                Label = t => Rulers.Get(t.Id)?.Shape == Rulers.Shape.Grid ? "Resize grid…" : "Change length…",
                IsVisible = t => t.Kind == TargetKind.Ruler,
                Execute = t => WithRuler(t, ruler => OpenSizer(ruler, false))
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "ruler.remove",
                Order = -1,
                Label = t => Rulers.Get(t.Id)?.Shape == Rulers.Shape.Grid ? "Remove grid" : "Remove line",
                IsVisible = t => t.Kind == TargetKind.Ruler,
                Execute = t => WithRuler(t, Rulers.Remove)
            });
        }

        private static void Add(string id, int order, string label, Rulers.Shape shape)
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = id,
                Order = order,
                Label = _ => label,
                IsVisible = t => t.Kind == TargetKind.Board || t.Kind == TargetKind.Ruler,
                Execute = t =>
                {
                    bool grid = shape == Rulers.Shape.Grid;
                    var ruler = Rulers.Add(shape, t.BoardPosition,
                        grid ? DefaultColumns : DefaultLength,
                        grid ? DefaultRows : DefaultLength);
                    OpenSizer(ruler, true);
                }
            });
        }

        // The ruler updates live as the sliders move; Cancel puts it back (or removes a
        // new one).
        private static void OpenSizer(Rulers.Data ruler, bool isNew)
        {
            int oldColumns = ruler.Columns, oldRows = ruler.Rows;
            var fields = new List<SizerField>();
            SizerField columns = null, rows = null;
            if (ruler.Shape != Rulers.Shape.VerticalLine)
                fields.Add(columns = new SizerField(ruler.Shape == Rulers.Shape.Grid ? "W" : "Length", ruler.Columns, 1, Rulers.MaxCells));
            if (ruler.Shape != Rulers.Shape.HorizontalLine)
                fields.Add(rows = new SizerField(ruler.Shape == Rulers.Shape.Grid ? "H" : "Length", ruler.Rows, 1, Rulers.MaxCells));

            string title = (isNew ? "New " : "") + (ruler.Shape == Rulers.Shape.Grid ? "grid" : "line");
            title = char.ToUpperInvariant(title[0]) + title.Substring(1);
            string unit = ruler.Shape == Rulers.Shape.Grid ? "In cards" : ruler.Shape == Rulers.Shape.HorizontalLine ? "In card widths" : "In card heights";

            MarginaliaController.Instance.OpenSizer(title, unit + " · Alt + drag to move it later", fields,
                () => Rulers.Resize(ruler, columns?.Value ?? ruler.Columns, rows?.Value ?? ruler.Rows),
                Rulers.Save,
                () =>
                {
                    if (isNew)
                        Rulers.Remove(ruler);
                    else
                        Rulers.Resize(ruler, oldColumns, oldRows);
                },
                isNew ? (Action)null : () => Rulers.Remove(ruler));
        }

        private static void WithRuler(MenuTarget target, Action<Rulers.Data> action)
        {
            var ruler = Rulers.Get(target.Id);
            if (ruler != null)
                action(ruler);
        }
    }
}
