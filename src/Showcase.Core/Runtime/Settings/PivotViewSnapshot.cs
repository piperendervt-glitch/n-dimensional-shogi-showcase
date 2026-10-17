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
        public Q4 SelectedCell { get; }
        public Q4 PlaneReferenceCell { get; }

        public PivotViewSnapshot(double height, double[] center, double[] basis, double[] anchor,
            double halfWindow, double cellScale, double[] camera, string selectedPieceId,
            Q4 selectedCell, Q4 planeReferenceCell, string embeddingId = EmbeddingVersion)
        {
            if (embeddingId != EmbeddingVersion && embeddingId != LegacyBoardAtOriginVersion) throw new ArgumentException("未知の固定埋込方式です。");
            this.center = Copy(center, 4, "center"); this.basis = Copy(basis, 16, "basis");
            this.anchor = Copy(anchor, 3, "anchor"); this.camera = Copy(camera, 6, "camera");
            if (!Finite(height) || height < 0 || height > 1000 || !Finite(halfWindow) || halfWindow < .25 || halfWindow > 128 ||
                !Finite(cellScale) || cellScale < .0001 || cellScale > 10) throw new ArgumentException("埋込高さ・表示窓・単位長が範囲外です。");
            if (Math.Abs(this.camera[1]) > 75 || this.camera[2] < .24 || this.camera[2] > 2 || this.camera.Skip(3).Any(x => Math.Abs(x) > 1000))
                throw new ArgumentException("カメラのpitch・distance・panが表示範囲外です。");
            for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++)
            {
                double dot = 0; for (int i = 0; i < 4; i++) dot += this.basis[a * 4 + i] * this.basis[b * 4 + i];
                if (Math.Abs(dot - (a == b ? 1 : 0)) > 1e-6) throw new ArgumentException("4D基底は1e-6以内の正規直交基底が必要です。");
            }
            if (Determinant(this.basis) < 1 - 4e-6) throw new ArgumentException("4D基底は正の向きが必要です。反転姿勢は読み込みません。");
            if (selectedPieceId == null || selectedPieceId.Length > 128 || selectedPieceId.Any(char.IsControl)) throw new ArgumentException("選択駒IDが不正です。");
            if (!selectedCell.InBounds || !planeReferenceCell.InBounds) throw new ArgumentException("表示の基準セルが論理盤域外です。");
            // Unity Y is up; lowering A by s*h keeps C, B and every piece where they were in 4D.
            if (embeddingId == LegacyBoardAtOriginVersion) this.anchor[1] -= height * cellScale;
            EmbeddingId = EmbeddingVersion; Height = height; HalfWindow = halfWindow; CellScale = cellScale;
            SelectedPieceId = selectedPieceId; SelectedCell = selectedCell; PlaneReferenceCell = planeReferenceCell;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double[] Copy(double[] values, int size, string label)
        {
            if (values == null || values.Length != size || values.Any(x => !Finite(x) || Math.Abs(x) > 1000000))
                throw new ArgumentException(label + "の次元・有限範囲が不正です。");
            return (double[])values.Clone();
        }
        private static double Determinant(double[] matrix)
        {
            double det = 0;
            // Leibniz expansion is tiny at 4D and independent of the rendering algebra.
            for (int a = 0; a < 4; a++) for (int b = 0; b < 4; b++) for (int c = 0; c < 4; c++) for (int d = 0; d < 4; d++)
            {
                if (a == b || a == c || a == d || b == c || b == d || c == d) continue;
                int inversions = (a > b ? 1 : 0) + (a > c ? 1 : 0) + (a > d ? 1 : 0) + (b > c ? 1 : 0) + (b > d ? 1 : 0) + (c > d ? 1 : 0);
                det += (inversions % 2 == 0 ? 1 : -1) * matrix[a] * matrix[4 + b] * matrix[8 + c] * matrix[12 + d];
            }
            return det;
        }
    }
}
