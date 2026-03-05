namespace Autonome.Data;

/// <summary>
/// Registry of valid tags, step types, personality axes, and other vocabulary.
/// </summary>
public class VocabularyRegistry
{
    public HashSet<string> ValidStepTypes { get; } =
    [
        "moveTo",
        "animate",
        "wait",
        "modifyProperty",
        "emitDirective",
        "emitEvent",
        "socialInteraction",
        "createEntity",
        "destroyEntity"
    ];

    public HashSet<string> ValidModifierTypes { get; } =
    [
        "memory",
        "directive",
        "passive",
        "trait"
    ];

    public HashSet<string> ValidPriorities { get; } =
    [
        "low",
        "normal",
        "urgent",
        "critical"
    ];

    public HashSet<string> ValidScopes { get; } =
    [
        "subordinates",
        "peers",
        "siblings",
        "kin"
    ];

    public HashSet<string> ValidConditions { get; } =
    [
        "below_critical",
        "below",
        "above"
    ];
}
