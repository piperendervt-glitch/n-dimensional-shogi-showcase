using System;
using System.Linq;

namespace NDShogi.Showcase.Core.Settings
{
    // Persistence-only DTO. Core deliberately has no dependency on Geometry or Unity.
    // Center/basis use F,U,V,W; anchor/pan use Unity X,Y,Z in metres.
    public sealed class PivotViewSnapshot
    {
        public const string EmbeddingVersion = "core-v-plus-height/2";
        // /1 placed O on the wooden board, so its anchor sits s*h higher than /2 for the same view.
        public const string LegacyBoardAtOriginVersion = "core-v-plus-height/1";
        private readonly double[] center, basis, anchor, camera;
        public string EmbeddingId { get; }
        public double Height { get; }
        public double HalfWindow { get; }
        public double CellScale { get; }
        public double[] Center => (double[])center.Clone();
        public double[] Basis => (double[])basis.Clone();
        public double[] Anchor => (double[])anchor.Clone();
        public double[] Camera => (double[])camera.Clone();
        public string SelectedPieceId { get; }
        public QN SelectedCell { get; }
        public QN PlaneReferenceCell { get; }
        /// <summary>N of the saved view: the centre has N components and the basis N×N.</summary>
        public int Dimensions { get; }

        public PivotViewSnapshot(double height, double[] center, double[] basis, double[] anchor,
            double halfWindow, double cellScale, double[] camera, string selectedPieceId,
            QN selectedCell, QN planeReferenceCell, string embeddingId = EmbeddingVersion)
        {
            if (embeddingId != EmbeddingVersion && embeddingId != LegacyBoardAtOriginVersion) throw new ArgumentException("未知の固定埋込方式です。");
            // N is the centre's length (3..11); 4 is the original 4D view and its saved form is unchanged.
            int n = center == null ? 0 : center.Length;
            if (n < BoundsN.MinDimensions || n > BoundsN.MaxDimensions) throw new ArgumentException("centerの次元は3〜" + BoundsN.MaxDimensions + "です。");
            this.center = Copy(center!, n, "center"); this.basis = Copy(basis, n * n, "basis");
            this.anchor = Copy(anchor, 3, "anchor"); this.camera = Copy(camera, 6, "camera");
            if (!Finite(height) || height < 0 || height > 1000 || !Finite(halfWindow) || halfWindow < .25 || halfWindow > 128 ||
                !Finite(cellScale) || cellScale < .0001 || cellScale > 10) throw new ArgumentException("埋込高さ・表示窓・単位長が範囲外です。");
            if (Math.Abs(this.camera[1]) > 75 || this.camera[2] < .24 || this.camera[2] > 2 || this.camera.Skip(3).Any(x => Math.Abs(x) > 1000))
                throw new ArgumentException("カメラのpitch・distance・panが表示範囲外です。");
            for (int a = 0; a < n; a++) for (int b = 0; b < n; b++)
            {
                double dot = 0; for (int i = 0; i < n; i++) dot += this.basis[a * n + i] * this.basis[b * n + i];
                if (Math.Abs(dot - (a == b ? 1 : 0)) > 1e-6) throw new ArgumentException("N次元基底は1e-6以内の正規直交基底が必要です。");
            }
            if (Determinant(this.basis, n) < 1 - 4e-6) throw new ArgumentException("N次元基底は正の向きが必要です。反転姿勢は読み込みません。");
            if (selectedPieceId == null || selectedPieceId.Length > 128 || selectedPieceId.Any(char.IsControl)) throw new ArgumentException("選択駒IDが不正です。");
            if (!selectedCell.InBounds || !planeReferenceCell.InBounds || selectedCell.Rank > n || planeReferenceCell.Rank > n) throw new ArgumentException("表示の基準セルが論理盤域外です。");
            // Unity Y is up; lowering A by s*h keeps C, B and every piece where they were in 4D.
            if (embeddingId == LegacyBoardAtOriginVersion) this.anchor[1] -= height * cellScale;
            EmbeddingId = EmbeddingVersion; Height = height; HalfWindow = halfWindow; CellScale = cellScale;
            // Cells are stored with all N coordinates: a fresh match selects no cell (the padded origin), which must still save as "0,0,0,0".
            SelectedPieceId = selectedPieceId; SelectedCell = QN.Padded(selectedCell.ToArray(), n); PlaneReferenceCell = QN.Padded(planeReferenceCell.ToArray(), n); Dimensions = n;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double[] Copy(double[] values, int size, string label)
        {
            if (values == null || values.Length != size || values.Any(x => !Finite(x) || Math.Abs(x) > 1000000))
                throw new ArgumentException(label + "の次元・有限範囲が不正です。");
            return (double[])values.Clone();
        }
        // Gaussian elimination with partial pivoting, independent of the rendering algebra.
        internal static double Determinant(double[] matrix, int n)
        {
            var m = (double[])matrix.Clone(); double det = 1;
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
    }
}
