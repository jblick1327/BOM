using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Clipper2Lib;
using UnityEditor;
using UnityEngine;

namespace BOM.Editor
{
    public sealed class ImportProposal
    {
        public PieceDraft draft;
        public int sourceMeshes,sourceTriangles;
        public string inference;
        public ValidationReport report;
    }
    public static class ImportService
    {
        public static Matrix4x4 MeshToSource(GameObject source,MeshFilter filter) =>
            Matrix4x4.TRS(Vector3.zero,source.transform.localRotation,source.transform.localScale)
            *source.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix;
        public static Vector3 ToAuthoring(Vector3 point, ImportSettings settings)
        {
            point=Quaternion.Euler(settings.orientation)*point*settings.scale;
            var projected=settings.plane==ProjectionPlane.XY?point:settings.plane==ProjectionPlane.XZ
                ?new Vector3(point.x,point.z,point.y):new Vector3(point.y,point.z,point.x);
            return new Vector3((projected.x-settings.origin.x)*settings.projectedScale.x,
                (projected.y-settings.origin.y)*settings.projectedScale.y,projected.z);
        }
        public static Material SourceMaterial(GameObject source) => source==null?null:
            source.GetComponentsInChildren<MeshRenderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null);
        public static void ReadAppearance(Material material,FaceAppearance appearance)
        {
            if(material==null)return;
            if(material.HasProperty("_BaseColor"))appearance.face=material.GetColor("_BaseColor");
            else if(material.HasProperty("_Color"))appearance.face=material.GetColor("_Color");
            appearance.image=(material.HasProperty("_BaseMap")?material.GetTexture("_BaseMap"):
                material.HasProperty("_MainTex")?material.GetTexture("_MainTex"):null) as Texture2D;
        }
        public static ImportProposal Propose(GameObject source,PieceDefinition previous=null,bool regenerate=true,ImportSettings settings=null)
        {
            var result=new ImportProposal {draft=previous!=null?previous.Draft():new PieceDraft(),report=new ValidationReport()};
            if(source==null) {result.report.Add(previous,"Source","The original source is missing.","Select the original FBX model or restore its source reference.");return result;}
            string path=AssetDatabase.GetAssetPath(source);
            if(!string.Equals(Path.GetExtension(path),".fbx",StringComparison.OrdinalIgnoreCase))
            {result.report.Add(source,"Source format","v1 imports Blender-exported FBX assets.","Export FBX from Blender and select the imported model.");return result;}
            var draft=result.draft;
            if(settings!=null) draft.import=settings.Clone();
            draft.import.sourcePath=path;draft.import.sourceGuid=AssetDatabase.AssetPathToGUID(path);draft.import.sourceHash=AssetDatabase.GetAssetDependencyHash(path).ToString();
            if(!regenerate&&previous!=null)
            {result.inference="Keeping the approved profile, manual corrections, appearance and gameplay settings. Source dependency changes are pending review.";result.report=AuthoringValidation.Piece(draft,previous);return result;}
            if(Application.isPlaying)
            {result.report.Add(source,"Projection","Source mesh projection is an authoring operation; this FBX may not retain readable vertices during Play mode.","Stop the playtest before regenerating a source proposal. Existing approved profiles remain editable.");return result;}
            try
            {
                var paths=new Paths64();float depthMin=float.PositiveInfinity,depthMax=float.NegativeInfinity;
                var filters=source.GetComponentsInChildren<MeshFilter>(true);result.sourceMeshes=filters.Length;
                foreach(var filter in filters)
                {
                    var mesh=filter.sharedMesh;if(mesh==null) continue;
                    var transform=MeshToSource(source,filter);
                    var vertices=mesh.vertices.Select(p=>ToAuthoring(transform.MultiplyPoint3x4(p),draft.import)).ToArray();
                    foreach(var vertex in vertices){depthMin=Mathf.Min(depthMin,vertex.z);depthMax=Mathf.Max(depthMax,vertex.z);}
                    var triangles=mesh.triangles;result.sourceTriangles+=triangles.Length/3;
                    for(int i=0;i+2<triangles.Length;i+=3)
                    {
                        var polygon=new Path64(new[] {Geometry.Point(vertices[triangles[i]]),
                            Geometry.Point(vertices[triangles[i+1]]),Geometry.Point(vertices[triangles[i+2]])});
                        if(Math.Abs(Clipper.Area(polygon))<1) continue;
                        if(Clipper.Area(polygon)<0) polygon.Reverse();paths.Add(polygon);
                    }
                }
                if(paths.Count==0) throw new InvalidOperationException("No occupied area projects onto the selected plane. Change the plane/orientation or restore readable model geometry.");
                var regions=Geometry.Boolean(paths,null,ClipType.Union);if(regions.Count==0) throw new InvalidOperationException("Projection produced no closed material region.");
                draft.profile=regions[0];draft.disconnectedProposal=regions.Skip(1).ToList();draft.import.manuallyCorrected=false;
                if(previous==null)
                {
                    ReadAppearance(SourceMaterial(source),draft.appearance);
                    var points=draft.profile.outer.points;float x0=points.Min(p=>p.x),y0=points.Min(p=>p.y);
                    float width=points.Max(p=>p.x)-x0,height=points.Max(p=>p.y)-y0;
                    draft.appearance.u=new Vector2(1/width,0);draft.appearance.v=new Vector2(0,1/height);draft.appearance.offset=new Vector2(-x0/width,-y0/height);
                    draft.thickness=Mathf.Max(.05f,depthMax-depthMin);
                }
                var importer=AssetImporter.GetAtPath(path) as ModelImporter;
                result.inference="Unity FBX axes/unit conversion is applied before projection; importer global scale: "+(importer!=null?importer.globalScale:1)+". "
                    +result.sourceMeshes+" meshes / "+result.sourceTriangles+" source triangles -> "+regions.Count+" proposed connected regions. "
                    +"Plane "+draft.import.plane+", orientation "+draft.import.orientation+", scale "+draft.import.scale+" metres per Unity source unit, origin "+draft.import.origin+". "
                    +"One approved profile becomes one body. Appearance uses the first source material with an editable planar mapping; review all colours/images before approval.";
                result.report=AuthoringValidation.Piece(draft,previous!=null?(UnityEngine.Object)previous:source);
            }
            catch(Exception e) {result.report.Add(source,"Projection",e.Message,"Correct source plane/settings or enable readable mesh data in the FBX importer.");}
            return result;
        }
        public static GameObject Source(PieceDefinition piece)
        {
            if(piece==null) return null;var settings=piece.Draft().import;
            string path=AssetDatabase.GUIDToAssetPath(settings.sourceGuid);
            return string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }
        public static string ImagePng(Texture2D image)
        {
            if(image==null) return null;
            if(image.width>8192||image.height>8192) throw new InvalidOperationException("Face image exceeds 8192 pixels. Select a supported image size.");
            var previous=RenderTexture.active;var temporary=RenderTexture.GetTemporary(image.width,image.height,0,RenderTextureFormat.ARGB32);
            Texture2D copy=null;
            try
            {
                Graphics.Blit(image,temporary);RenderTexture.active=temporary;
                copy=new Texture2D(image.width,image.height,TextureFormat.RGBA32,false);
                copy.ReadPixels(new Rect(0,0,image.width,image.height),0,0);copy.Apply();return Convert.ToBase64String(copy.EncodeToPNG());
            }
            finally {RenderTexture.active=previous;RenderTexture.ReleaseTemporary(temporary);if(copy!=null) UnityEngine.Object.DestroyImmediate(copy);}
        }
        public static bool Approve(PieceDefinition target,PieceDraft draft,out ValidationReport report)
        {
            report=AuthoringValidation.Piece(draft,target);if(!report.IsValid) return false;
            var approved=draft.Clone();
            try {approved.appearance.approvedImagePng=ImagePng(approved.appearance.image);}
            catch(Exception e) {report.Add(target,"Face image",e.Message,"Select a valid face image or remove the image selection.");return false;}
            Undo.RecordObject(target,"Approve BOM piece");
            if(!target.TryApprove(approved,out report)) return false;
            EditorUtility.SetDirty(target);if(AssetDatabase.Contains(target))AssetDatabase.SaveAssetIfDirty(target);return true;
        }
    }
}
