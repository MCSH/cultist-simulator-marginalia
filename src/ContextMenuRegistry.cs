using System;
using System.Collections.Generic;
using System.Linq;

namespace MarginaliaMod
{
    public sealed class ContextMenuItem
    {
        public string Id;
        public int Order;
        public Func<NoteTarget, string> Label;
        public Func<NoteTarget, bool> IsVisible = _ => true;
        public Action<NoteTarget> Execute;
    }

    // Everything that shows up in the context menu is registered here, so new
    // features only need to add items rather than touch the menu UI.
    public static class ContextMenuRegistry
    {
        private static readonly List<ContextMenuItem> Items = new List<ContextMenuItem>();

        public static void Register(ContextMenuItem item)
        {
            Items.RemoveAll(i => i.Id == item.Id);
            Items.Add(item);
        }

        public static IEnumerable<ContextMenuItem> ItemsFor(NoteTarget target)
        {
            return Items.Where(i => i.IsVisible(target)).OrderBy(i => i.Order);
        }
    }

    public static class NoteMenuItems
    {
        public static void Register()
        {
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "note.edit",
                Order = 0,
                Label = t => NoteStore.Has(t) ? "Edit note…" : "Write note…",
                Execute = t => MarginaliaController.Instance.OpenEditor(t)
            });
            ContextMenuRegistry.Register(new ContextMenuItem
            {
                Id = "note.delete",
                Order = 10,
                Label = _ => "Erase note",
                IsVisible = NoteStore.Has,
                Execute = NoteStore.Delete
            });
        }
    }
}
