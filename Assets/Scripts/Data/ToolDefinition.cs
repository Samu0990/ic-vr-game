using UnityEngine;
using VRSurgery.Haptics;
using VRSurgery.Tools;

namespace VRSurgery.Data
{
    /// <summary>
    /// How a hand takes hold of an instrument. The two are different grips, not two settings of
    /// one grip: a pencil grip contacts the handle at three fingertips and leaves the other two
    /// folded away, while a power grip wraps every finger round it.
    /// </summary>
    public enum GripStyle
    {
        /// <summary>Pinched between thumb and index, resting on the middle finger. Scalpel, forceps, needle holder.</summary>
        Pencil = 0,

        /// <summary>Wrapped in the whole fist. Mallet, retractor, rib spreader.</summary>
        Power = 1,
    }

    /// <summary>
    /// How a hand closes round one particular instrument.
    ///
    /// A hand holding something is not a closed fist: what the fingers have to clear is the
    /// handle, and a 6 mm scalpel shank, a 9 mm forceps shank and a rib spreader are each
    /// cleared differently. Squeezing the grip button harder does not make a real hand close
    /// further through the steel, so while an instrument is held these closures come from the
    /// instrument instead of from the button.
    /// </summary>
    [System.Serializable]
    public struct HandHold
    {
        [Tooltip("How the hand takes hold. Pencil pinches the handle between thumb and index " +
                 "with the middle finger under it, which is how a scalpel, a pair of forceps or " +
                 "a needle holder is actually held. Power wraps the whole fist round it, for a " +
                 "mallet or a retractor.")]
        public GripStyle style;

        [Tooltip("How far the middle, ring and little fingers close round the handle. 1 is a " +
                 "full fist, which closes through anything thinner than a broom.")]
        [Range(0f, 1f)] public float gripClosure;

        [Tooltip("Where the index finger rests with the instrument held and the trigger off. " +
                 "A scalpel is steadied with the index laid along the shank, so it stays nearly " +
                 "straight. Pulling the trigger still curls it from here.")]
        [Range(0f, 1f)] public float indexClosure;

        [Tooltip("How far the thumb comes over. In a pencil grip the thumb opposes the fingers " +
                 "across the handle rather than wrapping round it.")]
        [Range(0f, 1f)] public float thumbClosure;

        [Tooltip("Radius of the handle where the hand closes on it, metres. 0 means measure it " +
                 "from the instrument's own collider instead of authoring it.")]
        [Range(0f, 0.05f)] public float handleRadius;

        [Tooltip("How far the fingers that do NOT touch the handle fold away. In a pencil grip " +
                 "the ring and little fingers tuck into the palm and hold nothing. 0 means use " +
                 "the sensible default for the style.")]
        [Range(0f, 1f)] public float tuckClosure;

        [Tooltip("Degrees the instrument is tipped forward in the fist. 0 lays it along the " +
                 "handle out past the thumb; 40 is the angle a scalpel or a pen makes with the " +
                 "hand that holds it.")]
        [Range(0f, 80f)] public float pitchDegrees;

        /// <summary>
        /// A pencil grip on a slim instrument. Used when a tool declares no hold of its own,
        /// which is still far closer than a full fist.
        /// </summary>
        public static HandHold Default => new HandHold
        {
            style = GripStyle.Pencil,
            gripClosure = 0.58f,
            indexClosure = 0.24f,
            thumbClosure = 0.5f,
            pitchDegrees = 40f,
            handleRadius = 0f,
            tuckClosure = 0.85f,
        };

        /// <summary>A whole-fist grip for saws, retractors and defibrillator paddles.</summary>
        public static HandHold Power => new HandHold
        {
            style = GripStyle.Power,
            gripClosure = 0.72f,
            indexClosure = 0.68f,
            thumbClosure = 0.62f,
            pitchDegrees = 12f,
            handleRadius = 0f,
            tuckClosure = 0f,
        };

        /// <summary>Whether the handle is pinched at the fingertips rather than wrapped in a fist.</summary>
        public bool IsPencil => style == GripStyle.Pencil;

        /// <summary>
        /// How far the fingers that touch nothing fold away. A pencil grip tucks them well in;
        /// a power grip has no such fingers, so they simply close with the rest of the fist.
        ///
        /// Zero means unauthored rather than straight, for the same reason as
        /// <see cref="IsConfigured"/>: a hold written before this field existed reads back as 0.
        /// </summary>
        public float TuckClosure =>
            tuckClosure > 0f ? tuckClosure : (IsPencil ? 0.85f : gripClosure);

        /// <summary>
        /// Whether this hold was actually authored. Unity deserialises a struct missing from a
        /// .asset as all zeros rather than as the C# default, so an all-zero hold means "not
        /// set" — never "hold this with a flat open hand".
        /// </summary>
        public bool IsConfigured =>
            gripClosure > 0f || indexClosure > 0f || thumbClosure > 0f || pitchDegrees > 0f;
    }

    [CreateAssetMenu(fileName = "ToolDefinition", menuName = "VRSurgery/Data/Tool Definition")]
    public class ToolDefinition : ScriptableObject
    {
        [SerializeField] private string toolId;
        [SerializeField] private string displayName;
        [SerializeField] private ToolType toolType;
        [SerializeField] private ToolCapability capabilities;
        [SerializeField] private HapticProfile hapticProfile;
        [SerializeField] private Vector3 gripPointLocalPosition;
        [SerializeField] private Vector3 gripPointLocalEulerAngles;
        [SerializeField] private HandHold hold = HandHold.Default;

        public string ToolId => toolId;
        public string DisplayName => displayName;
        public ToolType ToolType => toolType;
        public ToolCapability Capabilities => capabilities;
        public HapticProfile HapticProfile => hapticProfile;
        public Vector3 GripPointLocalPosition => gripPointLocalPosition;
        public Vector3 GripPointLocalEulerAngles => gripPointLocalEulerAngles;

        /// <summary>How a hand closes round this instrument. See <see cref="HandHold"/>.</summary>
        // Old transplant scenes embedded their ToolDefinitions before HandHold was serialized.
        // Resolve those all-zero values from the tool type so a saw is not held like a pencil and
        // a scalpel is not clenched like a hammer.
        public HandHold Hold => hold.IsConfigured ? hold : DefaultHoldFor(toolType);

        public bool HasCapability(ToolCapability capability) => (capabilities & capability) == capability;

        /// <summary>Builds a definition in memory. Used by the scene builder and by tests.</summary>
        public static ToolDefinition Create(
            string id,
            string name,
            ToolType type,
            ToolCapability toolCapabilities,
            HapticProfile haptics = null,
            HandHold? handHold = null)
        {
            ToolDefinition definition = CreateInstance<ToolDefinition>();
            definition.name = id;
            definition.toolId = id;
            definition.displayName = name;
            definition.toolType = type;
            definition.capabilities = toolCapabilities;
            definition.hapticProfile = haptics;
            definition.hold = handHold ?? DefaultHoldFor(type);
            return definition;
        }

        public static HandHold DefaultHoldFor(ToolType type) =>
            type == ToolType.Retractor || type == ToolType.Defibrillator
                ? HandHold.Power
                : HandHold.Default;
    }
}
