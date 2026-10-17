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
        public Vec4 Embedding => new Vec4(0, 0, EmbeddingHeight, 0);
        public SliceFrame Frame { get; }
        // Unity XYZ metres, unlike section-local (F,U,V) and logical Vec4 (F,U,V,W).
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
            double height = PrototypeHeight, double halfWindow = DefaultHalfWindow)
        {
            var center = new Vec4(0, 0, height, 0);
            // C=P is displayed on the board surface; O lies s*h below the wood and is never shown as the board.
            return new PivotViewState(height, SliceAxes.Reference(ReferenceView.XYZ, center),
                boardAnchor, cellScale, halfWindow);
        }

        public PivotViewState WithCenter(Vec4 center)
        {
            if (!center.IsFinite) throw new ArgumentException("Center must be finite.");
            var delta = center - Frame.Center;
            var local = new Vec3(Vec4.Dot(Frame.AxisX, delta), Vec4.Dot(Frame.AxisY, delta), Vec4.Dot(Frame.AxisZ, delta));
            return new PivotViewState(EmbeddingHeight,
                new SliceFrame(center, Frame.AxisX, Frame.AxisY, Frame.AxisZ, Frame.Normal),
                Anchor + MapSection(local) * CellScale, CellScale, HalfWindow);
        }

        public PivotViewState WithOrientation(SliceFrame frame)
        {
            if ((frame.Center - Frame.Center).Length > 1e-10)
                throw new ArgumentException("Orientation changes must preserve the current center.");
            return new PivotViewState(EmbeddingHeight, frame, Anchor, CellScale, HalfWindow);
        }

        public PivotViewState WithHalfWindow(double value) => new PivotViewState(EmbeddingHeight, Frame, Anchor, CellScale, value);
        public Vec4 CoreToWorld(Vec4 logical) => logical + Embedding;
        public Vec4 WorldToCore(Vec4 world) => world - Embedding;
        public Instance4D Embed(Instance4D logical) => new Instance4D(logical.Shape, CoreToWorld(logical.Position), logical.Forward, logical.Id);
        public Vec3 SectionToDisplay(Vec3 section) => Anchor + MapSection(section) * CellScale;
        public Vec3 DisplayToSection(Vec3 display) => UnmapSection((display - Anchor) / CellScale);
        // Projection supplies chart coordinates only; visibility still requires actual solid intersection and window clipping.
        public Vec3 WorldToDisplay(Vec4 world) => SectionToDisplay(Frame.Project(world));
        public Vec4 DisplayToWorld(Vec3 display) => Frame.Lift(DisplayToSection(display));
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

        public static double Determinant(SliceFrame frame)
        {
            var columns = new[] { frame.AxisX, frame.AxisY, frame.AxisZ, frame.Normal };
            double sum = 0;
            for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++) for (int c = 0; c < 4; c++) for (int d = 0; d < 4; d++)
            {
                if (a == b || a == c || a == d || b == c || b == d || c == d) continue;
                int inversions = (a > b ? 1 : 0) + (a > c ? 1 : 0) + (a > d ? 1 : 0) + (b > c ? 1 : 0) + (b > d ? 1 : 0) + (c > d ? 1 : 0);
                sum += (inversions % 2 == 0 ? 1 : -1) * Component(columns[a], 0) * Component(columns[b], 1) * Component(columns[c], 2) * Component(columns[d], 3);
            }
            return sum;
        }

        /// <summary>R_YW(vertical) R_XW(horizontal) F0; Ctrl+Shift substitutes ZW for XW.</summary>
        public static SliceFrame RotateDrag(SliceFrame start, double horizontalDegrees, double verticalDegrees, bool zInsteadOfX = false)
        {
            Validate(start);
            var first = Rotate(start, zInsteadOfX ? 0 : 1, 3, horizontalDegrees * Math.PI / 180);
            return Rotate(first, 2, 3, verticalDegrees * Math.PI / 180);
        }

        public static SliceFrame Rotate(SliceFrame frame, int firstCoreAxis, int secondCoreAxis, double radians)
        {
            if (firstCoreAxis < 0 || firstCoreAxis > 3 || secondCoreAxis < 0 || secondCoreAxis > 3 || firstCoreAxis == secondCoreAxis || double.IsNaN(radians) || double.IsInfinity(radians))
                throw new ArgumentException("A finite angle and two distinct Core axes are required.");
            return new SliceFrame(frame.Center, RotateVector(frame.AxisX, firstCoreAxis, secondCoreAxis, radians),
                RotateVector(frame.AxisY, firstCoreAxis, secondCoreAxis, radians), RotateVector(frame.AxisZ, firstCoreAxis, secondCoreAxis, radians),
                RotateVector(frame.Normal, firstCoreAxis, secondCoreAxis, radians));
        }

        internal static Vec4 RotateVector(Vec4 value, int a, int b, double radians)
        {
            var v = new[] { value.X, value.Y, value.Z, value.W };
            double c = Math.Cos(radians), s = Math.Sin(radians), x = v[a], y = v[b];
            v[a] = c * x - s * y; v[b] = s * x + c * y;
            return new Vec4(v[0], v[1], v[2], v[3]);
        }
        internal static double Component(Vec4 value, int axis) => axis == 0 ? value.X : axis == 1 ? value.Y : axis == 2 ? value.Z : value.W;
    }
}
