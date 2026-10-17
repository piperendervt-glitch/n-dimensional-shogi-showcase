using System;

namespace Showcase.Geometry.Interaction
{
    /// <summary>Immutable display chart. Logical coordinates never contain the fixed embedding height.</summary>
    public sealed class PivotViewState
    {
        // /2: the wooden board surface displays the piece plane Y=h (P), so the initial pieces rest on it.
        // /1 displayed O on the board and lifted every piece s*h above the wood.
        public const string EmbeddingId = "core-v-plus-height/2";
        public const double PrototypeHeight = 3, DefaultHalfWindow = 4.5;
        public double EmbeddingHeight { get; }
        public VecN Embedding => new VecN(0, 0, EmbeddingHeight, 0);
        public SliceFrame Frame { get; }
        // Unity XYZ metres, unlike section-local (F,U,V) and logical VecN (F,U,V,W).
        public Vec3 Anchor { get; }
        public double HalfWindow { get; }
        public double CellScale { get; }

        public PivotViewState(double embeddingHeight, SliceFrame frame, Vec3 anchor,
            double cellScale = ShapeFactory.CellPitchMetres, double halfWindow = DefaultHalfWindow)
        {
            if (!Finite(embeddingHeight) || embeddingHeight < 0 || !anchor.IsFinite ||
                !Finite(cellScale) || cellScale <= 0 || !Finite(halfWindow) || halfWindow <= 0)
                throw new ArgumentException("Display embedding, anchor, scale and window must be finite and in range.");
            SliceRotations.Validate(frame);
            EmbeddingHeight = embeddingHeight; Frame = frame; Anchor = anchor;
            CellScale = cellScale; HalfWindow = halfWindow;
        }

        public static PivotViewState CreateInitial(Vec3 boardAnchor, double cellScale = ShapeFactory.CellPitchMetres,
            double height = PrototypeHeight, double halfWindow = DefaultHalfWindow, int dimensions = 4)
        {
            var center = VecN.Zero(dimensions).WithAxis(2, height);
            // C=P is displayed on the board surface; O lies s*h below the wood and is never shown as the board.
            // For N=4 this is exactly the former XYZ reference frame (F,U,V shown, W the normal).
            return new PivotViewState(height, SliceAxes.ReferenceFrame(SliceAxes.ShownAxes(ReferenceView.XYZ), dimensions, center),
                boardAnchor, cellScale, halfWindow);
        }
        /// <summary>N of the sliced space: the frame's three axes plus its normals.</summary>
        public int Dimensions => Frame.Dimensions;

        public PivotViewState WithCenter(VecN center)
        {
            if (!center.IsFinite) throw new ArgumentException("Center must be finite.");
            var delta = center - Frame.Center;
            var local = new Vec3(VecN.Dot(Frame.AxisX, delta), VecN.Dot(Frame.AxisY, delta), VecN.Dot(Frame.AxisZ, delta));
            return new PivotViewState(EmbeddingHeight, Frame.WithCenter(center),
                Anchor + MapSection(local) * CellScale, CellScale, HalfWindow);
        }

        public PivotViewState WithOrientation(SliceFrame frame)
        {
            if ((frame.Center - Frame.Center).Length > 1e-10)
                throw new ArgumentException("Orientation changes must preserve the current center.");
            return new PivotViewState(EmbeddingHeight, frame, Anchor, CellScale, HalfWindow);
        }

        public PivotViewState WithHalfWindow(double value) => new PivotViewState(EmbeddingHeight, Frame, Anchor, CellScale, value);
        public VecN CoreToWorld(VecN logical) => logical + Embedding;
        public VecN WorldToCore(VecN world) => world - Embedding;
        public InstanceN Embed(InstanceN logical) => new InstanceN(logical.Shape, CoreToWorld(logical.Position), logical.Forward, logical.Id);
        public Vec3 SectionToDisplay(Vec3 section) => Anchor + MapSection(section) * CellScale;
        public Vec3 DisplayToSection(Vec3 display) => UnmapSection((display - Anchor) / CellScale);
        // Projection supplies chart coordinates only; visibility still requires actual solid intersection and window clipping.
        public Vec3 WorldToDisplay(VecN world) => SectionToDisplay(Frame.Project(world));
        public VecN DisplayToWorld(Vec3 display) => Frame.Lift(DisplayToSection(display));
        public static Vec3 MapSection(Vec3 section) => new Vec3(section.Y, section.Z, section.X);
        public static Vec3 UnmapSection(Vec3 unity) => new Vec3(unity.Z, unity.X, unity.Y);
        static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public static class SliceRotations
    {
        public static void Validate(SliceFrame frame)
        {
            if (!frame.IsOrthonormal(1e-6) || Determinant(frame) < 1 - 4e-6)
                throw new ArgumentException("A finite positively oriented orthonormal 4D frame is required.");
        }

        // Determinant of the N×N matrix whose columns are the frame's basis vectors (Gaussian elimination with pivoting).
        public static double Determinant(SliceFrame frame)
        {
            var columns = frame.Vectors; int n = columns.Length; var m = new double[n * n];
            for (int row = 0; row < n; row++) for (int col = 0; col < n; col++) m[row * n + col] = columns[col].Axis(row);
            double det = 1;
            for (int col = 0; col < n; col++)
            {
                int pivot = col; for (int r = col + 1; r < n; r++) if (Math.Abs(m[r * n + col]) > Math.Abs(m[pivot * n + col])) pivot = r;
                if (Math.Abs(m[pivot * n + col]) < 1e-300) return 0;
                if (pivot != col) { for (int k = 0; k < n; k++) { double t = m[col * n + k]; m[col * n + k] = m[pivot * n + k]; m[pivot * n + k] = t; } det = -det; }
                det *= m[col * n + col];
                for (int r = col + 1; r < n; r++) { double f = m[r * n + col] / m[col * n + col]; for (int k = col; k < n; k++) m[r * n + k] -= f * m[col * n + k]; }
            }
            return det;
        }

        /// <summary>R_YH(vertical) R_XH(horizontal) F0 where H is the hidden core axis being rotated against (W by default); Ctrl+Shift substitutes ZH for XH.</summary>
        public static SliceFrame RotateDrag(SliceFrame start, double horizontalDegrees, double verticalDegrees, bool zInsteadOfX = false, int hiddenCoreAxis = 3)
        {
            Validate(start);
            var first = Rotate(start, zInsteadOfX ? 0 : 1, hiddenCoreAxis, horizontalDegrees * Math.PI / 180);
            return Rotate(first, 2, hiddenCoreAxis, verticalDegrees * Math.PI / 180);
        }

        public static SliceFrame Rotate(SliceFrame frame, int firstCoreAxis, int secondCoreAxis, double radians)
        {
            int n = frame.Dimensions;
            if (firstCoreAxis < 0 || firstCoreAxis >= n || secondCoreAxis < 0 || secondCoreAxis >= n || firstCoreAxis == secondCoreAxis || double.IsNaN(radians) || double.IsInfinity(radians))
                throw new ArgumentException("A finite angle and two distinct Core axes of the frame's space are required.");
            var vectors = frame.Vectors;
            for (int i = 0; i < vectors.Length; i++) vectors[i] = RotateVector(vectors[i], firstCoreAxis, secondCoreAxis, radians);
            return SliceFrame.FromVectors(frame.Center, vectors);
        }

        internal static VecN RotateVector(VecN value, int a, int b, double radians)
        {
            var v = value.ToArray(Math.Max(value.Dimensions, Math.Max(a, b) + 1));
            double c = Math.Cos(radians), s = Math.Sin(radians), x = v[a], y = v[b];
            v[a] = c * x - s * y; v[b] = s * x + c * y;
            return new VecN(v);
        }
        internal static double Component(VecN value, int axis) => value.Axis(axis);
    }
}
