# Showcase.Geometry

Unity-independent, C# 9 / netstandard2.1 geometry. The same Runtime sources compile as a local Unity package through `Showcase.Geometry.asmdef`. No external NuGet packages or UnityEngine references are used.

Coordinates are `(f,u,v,w)`, measured in cells, with vertical `v`. One cell is 0.05 metres. A section vertex's `Position` is `(f, rotated-horizontal, v)`; the Unity adapter maps it to `(Position.Y, Position.Z, Position.X) * cellPitch + anchor`. This cyclic permutation preserves orientation. Triangles are outward wound and normals refer to the restricted source halfspace.

```csharp
var shape = ShapeFactory.CreatePawn(ShapeVariant.C);
var instance = new Instance4D(shape, new Vec4(2, 0, 1, 0), 1, "hero");
var section = SectionBuilder.Build(instance, SliceFrame.AtAngle(45));
```

`ShapeFactory.CreatePiece(variant, size)` makes a pentagonal wedge with geometric bevels. Its size is fixed at creation. A extrudes that 3D solid through `w=±0.2*size`. C duplicates every width constraint in `w`; its central 0° and 90° sections agree, and the intermediate widening remains present. The board is a finite beveled 4D box. Neither source shape changes with observation angle.

`SectionBuilder` restricts the fixed 4D inequalities to the orthonormal observation frame, then clips a conservative bounding polyhedron in double precision. Generated cap faces retain the source constraint identity. Coincident constraints, low-dimensional feasible intersections, empty intersections, invalid numerical input, and capacity failures are distinct. An A extrusion cut exactly at a w endpoint retains its full three-dimensional volume. The implementation does not substitute a thick observation slab.

Each generated vertex stores its original `LocalPosition` in four dimensions, source `FaceId`, and `SurfaceKind`. `Top` identifies the original upper boundary; bevels and w end surfaces are separate. A reference material can sample `M3(f,u,v)`. The C visual material can sample the fixed affine field `M3(f,u+w,v)` to agree at both central endpoints. Both fields are independent of camera and observation angle. Glyphs belong to the source Top surface; no new glyph is centered on a newly generated face.

`TriplePlaneOracle` is the independent construction used by the console tests. It solves all triples of restricted source planes and checks every candidate against all constraints, without the primary builder's seed, clipping, or cap-generation code. Its dimensional rank uses an independent Gram-Schmidt routine. The tests compare vertices and known volumes, then separately verify finite coordinates, source inequalities, material-coordinate reconstruction, outward normals, duplicate triangles, edge incidence and Euler characteristic.

Run from repository root:

```powershell
dotnet run --project tests/Showcase.Geometry.Tests/Showcase.Geometry.Tests.csproj --configuration Release -- evidence/geometry
```

The suite covers full angle and return sweeps, fixed-section w movement, A/C comparisons, exact endpoint coincidence, point/line/plane contacts, empty components, malformed frames, capacity failure and float transfer. Results are written to `evidence/geometry/geometry-test-report.json`.

Runtime clipping currently uses managed lists and arrays. Cache unchanged sections at the adapter and profile the complete Unity scene before choosing buffer reuse or Jobs/Burst work. The console test duration is not a rendering benchmark. Unity shadows, reflections, temporal history, image quality and human acceptance remain separate checks.
