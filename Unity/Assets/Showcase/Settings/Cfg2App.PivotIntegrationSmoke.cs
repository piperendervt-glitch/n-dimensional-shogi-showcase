using System;
using System.IO;
using System.Linq;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using Showcase.Geometry.Interaction;
using NDShogi.Showcase.Core.Settings;

namespace Showcase.Unity.Settings
{
    public sealed partial class Cfg2App
    {
        IEnumerator PivotInputChecks()
        {
            var original=PivotDump("input-original");var originalSlice=slice.DisplayState;
            var gizmo=gizmos.Single(g=>g.gameObject.activeInHierarchy);var axis=LogicalAxis.X;var projected=gizmo.AxisScreenDirection(axis);var direction=projected.normalized;
            var handle=gizmo.Buttons[axis];var surface=handle.GetComponent<CfgPointerSurface>();var rect=(RectTransform)handle.transform;
            var start=RectTransformUtility.WorldToScreenPoint(null,rect.TransformPoint(rect.rect.center));var e=SmokePointer2(start,771);
            surface.OnPointerDown(e);e.position=start+direction*32*canvas.scaleFactor;surface.OnDrag(e);surface.OnPointerUp(e);RenderGeometry();
            // uGUI endpoints and canvas division are Float32. At 1080p their subpixel rounding
            // can exceed 1e-6 cells. Keep the ideal one-cell expectation within 0.00032 canvas px,
            // and independently check the actual quantized endpoint projection much more tightly.
            double dx=(double)(e.position.x/canvas.scaleFactor)-(double)(start.x/canvas.scaleFactor);
            double dy=(double)(e.position.y/canvas.scaleFactor)-(double)(start.y/canvas.scaleFactor);
            double projectedLength=Math.Sqrt((double)projected.x*projected.x+(double)projected.y*projected.y);
            double expectedAmount=(dx*projected.x+dy*projected.y)/(32*projectedLength);
            double actualAmount=slice.Center.Y-original.center[1];
            Record("SmokeAxisQuantization",$"start={start:R};end={e.position:R};projected={projected:R};scale={canvas.scaleFactor:R};expected={expectedAmount:R};actual={actualAmount:R};idealError={Math.Abs(actualAmount-1):R}");
            SmokeCheck(Math.Abs(actualAmount-expectedAmount)<1e-9,"PV10 axis delta matches independent Float32 endpoint projection within 1e-9 cells","actual uGUI endpoints, independent double projection");
            SmokeCheck(Math.Abs(actualAmount-1)<1e-5&&SameNumbers(original.basis,PivotDump("axis-moved").basis),"PV10 settings existing gizmo X drag moves C by one world cell without changing B","simulated uGUI handle events; ideal error < 0.00032 canvas px");
            SmokeCheck(IsPivotHomeSurface(start)&&IsPivotHomeSurface(RectTransformUtility.WorldToScreenPoint(null,previewRect.TransformPoint(previewRect.rect.center))),"PV11 settings gizmo and central 4D preview own Home shortcuts");
            slice.Restore(originalSlice);RenderGeometry();
            var board=RectTransformUtility.WorldToScreenPoint(null,previewRect.TransformPoint(previewRect.rect.center));e=SmokePointer2(board,772,PointerEventData.InputButton.Right);BeginPointer(e,false,false);e.position=board+new Vector2(50,20);PreviewDrag(e);
            actions["back"].onClick.Invoke();SmokeCheck(opened&&ownership.Owner==InputOwner.CameraOrbit,"PV10 unrelated button is rejected while right drag owns input");
            OnApplicationFocus(false);SmokeCheck(ownership.Owner==InputOwner.None&&SamePivot(original,PivotDump("focus-restored")),"PV10 focus loss restores full gesture state");OnApplicationFocus(true);
            e=SmokePointer2(board,773);BeginPointer(e,true,false);e.position=board+new Vector2(60,0);PreviewDrag(e);ShowMenu();
            SmokeCheck(ownership.Owner==InputOwner.None&&SameNumbers(original.basis,CapturePivotView().Basis),"PV10 screen transition cancels gesture before animator/screen switch");
            OpenSettings();RenderGeometry();yield return null;
        }
        IEnumerator PivotRecoverySteps(string phase)
        {
            string rejected=Path.Combine(runtimeRoot,"pivot-next-match.xml");var original=File.ReadAllBytes(rejected);
            if(phase=="pivot-recovery")
            {
                SmokeCheck(startupLoadError!=""&&config==null,"PV13 damaged original is rejected without fallback");OpenSettings();yield return null;
                draft.Edit(d=>d.Name="Recovered current edit");AfterEdit();slice.SetOrientation(SliceRotations.RotateDrag(slice.Frame,21,-13));slice.Translate(LogicalAxis.X,.3);yaw=41;pitch=22;UpdateCamera();RenderGeometry();
                var before=PivotDump("recovery-before");RecoverToNewBundle();RenderGeometry();var after=PivotDump("recovery-after");
                SmokeCheck(startupLoadError==""&&config!=null&&validPath!=rejected&&SamePivot(before,after)&&before.logicalHash==after.logicalHash&&original.SequenceEqual(File.ReadAllBytes(rejected)),"PV13 recovery commits new selected bundle while preserving rejected original and current edit/view");
                File.WriteAllText(Path.Combine(runtimeRoot,"recovery-expected.json"),JsonUtility.ToJson(after,true));File.WriteAllText(Path.Combine(runtimeRoot,"recovery-path.txt"),validPath);
                yield return SmokeCapture("recovery-preserved-edit");
            }
            else
            {
                var expected=JsonUtility.FromJson<PivotEvidenceState>(File.ReadAllText(Path.Combine(runtimeRoot,"recovery-expected.json")));
                SmokeCheck(startupLoadError==""&&config!=null&&validPath==File.ReadAllText(Path.Combine(runtimeRoot,"recovery-path.txt"))&&SamePivot(expected,PivotDump("recovery-reload"))&&expected.logicalHash==PivotDump("recovery-reload").logicalHash,"PV13 separate process resolves recovered bundle with exact logical and arbitrary view state");
                OpenSettings();RenderGeometry();yield return SmokeCapture("recovered-separate-process");
            }
            WritePlayerIdentity("SettingsRecovery");
        }
    }
}
