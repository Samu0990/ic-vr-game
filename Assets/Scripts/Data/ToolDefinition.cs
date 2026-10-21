using UnityEngine;
using VRSurgery.Haptics;
using VRSurgery.Tools;

namespace VRSurgery.Data
{
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

        [Tooltip("Degrees the instrument is tipped forward in the fist. 0 lays it along the " +
                 "handle out past the thumb; positive values lower its working end. Negative " +
                 "values support pistol grips whose attach frame is turned relative to the tool.")]
        [Range(-80f, 80f)] public float pitchDegrees;

        [Tooltip("Degrees the working end turns sideways in the hand. A pen-like scalpel follows " +
                 "the extended index finger instead of the controller's forward axis.")]
        [Range(-180f, 180f)] public float yawDegrees;

        [Tooltip("Use a three-finger precision pinch: thumb, index and middle touch the handle; " +
                 "ring and little keep the authored gripClosure instead of being pulled onto it.")]
        public bool precisionPinch;

        [Tooltip("Small local correction that seats this handle between the fingers. X is across " +
                 "the palm, Y is up and Z follows the hand grip frame; X mirrors on the left hand.")]
        public Vector3 seatOffset;

        [Tooltip("In a precision pinch, how much of the thumb and index closure goes to the " +
                 "knuckle at the hand. 1 bends the whole finger together, which is a fist. A " +
                 "pencil grip needs a low value: the knuckle stays nearly straight and only the " +
                 "phalanges curl, which is the only way the fingertip reaches the shank without " +
                 "clenching. 0 means unset and behaves as 1.")]
        [Range(0f, 1f)] public float pinchKnuckleShare;

        [Tooltip("Where the ring and little fingers rest in a precision pinch. They hold no " +
                 "instrument, so they tuck into the palm instead of hanging half open beside " +
                 "it. 0 means unset and falls back to gripClosure.")]
        [Range(0f, 1f)] public float tuckClosure;

        /// <summary>
        /// A pencil grip on a slim instrument. Used when a tool declares no hold of its own,
        /// which is still far closer than a full fist.
        /// </summary>
        public static HandHold Default => new HandHold
        {
            gripClosure = 0.58f,
            indexClosure = 0.24f,
            thumbClosure = 0.5f,
            pitchDegrees = 40f,
            yawDegrees = 0f,
            handleRadius = 0f,
            precisionPinch = false,
            seatOffset = Vector3.zero,
            pinchKnuckleShare = 0f,
            tuckClosure = 0f,
        };

        /// <summary>How much of a pinching finger's closure reaches its knuckle. See the field.</summary>
        public float KnuckleShare => pinchKnuckleShare > 0f ? pinchKnuckleShare : 1f;

        /// <summary>Where the two free fingers rest. See the field.</summary>
        public float Tuck => tuckClosure > 0f ? tuckClosure : gripClosure;

        /// <summary>
        /// Whether this hold was actually authored. Unity deserialises a struct missing from a
        /// .asset as all zeros rather than as the C# default, so an all-zero hold means "not
        /// set" — never "hold this with a flat open hand".
        /// </summary>
        public bool IsConfigured =>
            gripClosure > 0f || indexClosure > 0f || thumbClosure > 0f || pitchDegrees != 0f || yawDegrees != 0f ||
            precisionPinch || seatOffset != Vector3.zero || pinchKnuckleShare > 0f ||
            tuckClosure > 0f;
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
        public HandHold Hold => hold;

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
            definition.hold = handHold ?? HandHold.Default;
            return definition;
        }
    }
}
