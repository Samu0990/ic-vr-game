using UnityEngine;

namespace VRSurgery.Transplant
{
    /// <summary>
    /// The patient's body as something the code can ask about: where the skin is, and what lies
    /// under it.
    ///
    /// The skin has no collider, and cannot usefully have one: the scalpel has to sink into it,
    /// the hearts have to pass through the operative window, and a body-shaped mesh collider is
    /// too costly for a headset. So anything let go over the patient falls straight through to
    /// the table and lies inside the body — out of sight, yet "within reach" by distance, which
    /// is how a dropped heart used to vanish for the rest of the turn. This answers the two
    /// questions that matter instead: is a point hidden in there, and has a held object gone
    /// into the closed body outside the window.
    ///
    /// Baked by the scene builder from the skin mesh: a coarse field of the skin's top surface,
    /// plus the operative window and the floor of the open chest. The window column above that
    /// floor is not "inside": it is the open chest, where the hearts and the instruments belong.
    /// </summary>
    public class PatientBodyVolume : MonoBehaviour
    {
        /// <summary>Skin height stored where there is no body.</summary>
        public const float NoSkin = -1000f;

        [Header("Skin, from above")]
        [Tooltip("World X and Z of the first cell's corner.")]
        [SerializeField] private Vector2 origin;

        [SerializeField, Min(0.005f)] private float cell = 0.02f;
        [SerializeField, Min(0)] private int columns;
        [SerializeField, Min(0)] private int rows;

        [Tooltip("Highest skin point in each cell, world Y, row by row along Z. NoSkin off the body.")]
        [SerializeField] private float[] skinTop = new float[0];

        [Tooltip("World Y of the table top the patient lies on.")]
        [SerializeField] private float tableTop = 0.95f;

        [Header("Operative window")]
        [SerializeField] private Vector2 windowCentre;
        [SerializeField] private Vector2 windowHalfSize;

        [Tooltip("World Y of the open chest's floor: a heart resting on it is in the chest, not lost in the body.")]
        [SerializeField] private float chestFloor;

        /// <summary>Height of the skin's top surface at (x, z), or NoSkin off the body.</summary>
        public float SkinTop(float x, float z)
        {
            if (skinTop == null || columns <= 0 || rows <= 0) { return NoSkin; }

            int ix = Mathf.FloorToInt((x - origin.x) / cell);
            int iz = Mathf.FloorToInt((z - origin.y) / cell);
            if (ix < 0 || iz < 0 || ix >= columns || iz >= rows) { return NoSkin; }
            return skinTop[iz * columns + ix];
        }

        /// <summary>Inside the operative window's footprint, widened by <paramref name="margin"/>.</summary>
        public bool InWindow(Vector3 point, float margin) =>
            Mathf.Abs(point.x - windowCentre.x) < windowHalfSize.x + margin &&
            Mathf.Abs(point.z - windowCentre.y) < windowHalfSize.y + margin;

        /// <summary>Over the body, above the table and at least <paramref name="depth"/> below the skin.</summary>
        public bool UnderSkin(Vector3 point, float depth)
        {
            float top = SkinTop(point.x, point.z);
            return top > NoSkin && point.y > tableTop - 0.03f && point.y < top - depth;
        }

        /// <summary>
        /// Lying somewhere nobody can see it: under the skin, and not in the open chest on or above
        /// its floor. Where a dropped object counts as lost.
        /// </summary>
        public bool Hides(Vector3 point) =>
            UnderSkin(point, 0.01f) && !(InWindow(point, 0f) && point.y >= chestFloor - 0.015f);

        /// <summary>
        /// Inside the closed body, outside the window and a margin around it: where a held heart
        /// or instrument has no business going. The margin lets the work at the window's edge —
        /// the wound, the paddles around a heart wider than the window — happen undisturbed.
        /// </summary>
        public bool InsideClosedBody(Vector3 point, float windowMargin) =>
            UnderSkin(point, 0.015f) && !InWindow(point, windowMargin);

        public Vector2 WindowCentre => windowCentre;
        public Vector2 WindowHalfSize => windowHalfSize;

        private bool HasSkin(int ix, int iz) =>
            ix >= 0 && iz >= 0 && ix < columns && iz < rows && skinTop[iz * columns + ix] > NoSkin;

        /// <summary>
        /// Every edge of the body's outline seen from above, as pairs of world points (X, Z; Y is
        /// the table top). Drawn on the projector while it is being lined up with the mannequin.
        /// </summary>
        public void ForEachOutlineEdge(System.Action<Vector3, Vector3> visit)
        {
            if (visit == null || skinTop == null || skinTop.Length < columns * rows) { return; }

            for (int iz = 0; iz < rows; iz++)
            {
                for (int ix = 0; ix < columns; ix++)
                {
                    if (!HasSkin(ix, iz)) { continue; }

                    float x0 = origin.x + ix * cell, x1 = x0 + cell;
                    float z0 = origin.y + iz * cell, z1 = z0 + cell;
                    if (!HasSkin(ix - 1, iz)) { visit(new Vector3(x0, tableTop, z0), new Vector3(x0, tableTop, z1)); }
                    if (!HasSkin(ix + 1, iz)) { visit(new Vector3(x1, tableTop, z0), new Vector3(x1, tableTop, z1)); }
                    if (!HasSkin(ix, iz - 1)) { visit(new Vector3(x0, tableTop, z0), new Vector3(x1, tableTop, z0)); }
                    if (!HasSkin(ix, iz + 1)) { visit(new Vector3(x0, tableTop, z1), new Vector3(x1, tableTop, z1)); }
                }
            }
        }

        /// <summary>Stores the baked body. Used by the scene builder and by tests.</summary>
        public void Bake(Vector2 firstCellCorner, float cellSize, int columnCount, int rowCount, float[] heights,
            float tableTopY, Vector2 window, Vector2 windowHalf, float openChestFloorY)
        {
            origin = firstCellCorner;
            cell = Mathf.Max(0.005f, cellSize);
            columns = Mathf.Max(0, columnCount);
            rows = Mathf.Max(0, rowCount);
            skinTop = heights ?? new float[0];
            tableTop = tableTopY;
            windowCentre = window;
            windowHalfSize = windowHalf;
            chestFloor = openChestFloorY;
        }
    }
}
