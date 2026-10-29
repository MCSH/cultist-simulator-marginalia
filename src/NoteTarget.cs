namespace MarginaliaMod
{
    public enum TargetKind
    {
        Element,
        Verb
    }

    // The thing a context menu was opened on. Cards and aspects are both Elements in
    // the compendium, so a note on an aspect is the same note as on a card of that id.
    public sealed class NoteTarget
    {
        public TargetKind Kind { get; }
        public string Id { get; }
        public string Label { get; }

        public NoteTarget(TargetKind kind, string id, string label)
        {
            Kind = kind;
            Id = id;
            Label = string.IsNullOrEmpty(label) ? id : label;
        }

        public string Key => (Kind == TargetKind.Verb ? "verb:" : "element:") + Id;

        public string KindLabel => Kind == TargetKind.Verb ? "Verb" : "Element";
    }
}
