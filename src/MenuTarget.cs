using UnityEngine;

namespace MarginaliaMod
{
    public enum TargetKind
    {
        Element,
        Verb,
        Board,
        BoardLabel,
        Ruler
    }

    // The thing a context menu was opened on. Cards and aspects are both Elements in
    // the compendium, so a note on an aspect is the same note as on a card of that id.
    public sealed class MenuTarget
    {
        public TargetKind Kind { get; private set; }
        public string Id { get; private set; }
        public string Label { get; private set; }

        // Board and Ruler: where the click landed, in tabletop-local units.
        public Vector2 BoardPosition { get; private set; }

        public static MenuTarget ForElement(string id, string label) =>
            new MenuTarget { Kind = TargetKind.Element, Id = id, Label = string.IsNullOrEmpty(label) ? id : label };

        public static MenuTarget ForVerb(string id, string label) =>
            new MenuTarget { Kind = TargetKind.Verb, Id = id, Label = string.IsNullOrEmpty(label) ? id : label };

        public static MenuTarget ForBoard(Vector2 position) =>
            new MenuTarget { Kind = TargetKind.Board, Id = "board", Label = "The board", BoardPosition = position };

        public static MenuTarget ForBoardLabel(BoardLabels.Data label) =>
            new MenuTarget { Kind = TargetKind.BoardLabel, Id = label.Id, Label = FirstLine(label.Text) };

        public static MenuTarget ForRuler(Rulers.Data ruler, Vector2 position) =>
            new MenuTarget { Kind = TargetKind.Ruler, Id = ruler.Id, Label = Rulers.Describe(ruler), BoardPosition = position };

        public bool CanHaveNote => Kind == TargetKind.Element || Kind == TargetKind.Verb;

        public string NoteKey => (Kind == TargetKind.Verb ? "verb:" : "element:") + Id;

        public string Subtitle
        {
            get
            {
                switch (Kind)
                {
                    case TargetKind.Element: return "Element · " + Id;
                    case TargetKind.Verb: return "Verb · " + Id;
                    case TargetKind.BoardLabel: return "Board label · Alt + drag to move";
                    case TargetKind.Ruler: return "Ruler · Alt + drag to move";
                    default: return "Written on the table itself";
                }
            }
        }

        private static string FirstLine(string text)
        {
            text = (text ?? "").Trim();
            int newline = text.IndexOf('\n');
            if (newline >= 0)
                text = text.Substring(0, newline).TrimEnd() + "…";
            return text.Length > 60 ? text.Substring(0, 60).TrimEnd() + "…" : text;
        }
    }
}
