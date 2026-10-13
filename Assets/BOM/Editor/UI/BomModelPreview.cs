using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace BOM.Editor
{
    public sealed class BomModelPreview : IDisposable
    {
        public PreviewRenderUtility Utility { get; } = new PreviewRenderUtility();
        public bool HasSource { get; private set; }
        private GameObject root, geometry, overlay;
        private readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        private string geometryKey;
        private float overlayDepth;
        private Bounds bounds;
        private Material lineMaterial;
        private Texture lastTexture;
        private static bool rendering;

        public BomModelPreview()
        {
            Utility.camera.clearFlags=CameraClearFlags.SolidColor;
            Utility.camera.backgroundColor=new Color(.07f,.1f,.13f);
            Utility.lights[0].intensity=1.2f;Utility.lights[1].intensity=.6f;
        }
        public Texture Render(GameObject source,PieceDraft draft,BomProfileEditor editor,bool showSource,Vector2 orbit,Vector2 size,bool dirty)
        {
            if(!Geometry.Finite(size)||size.x<1||size.y<1||rendering)return lastTexture;
            if(!Geometry.Positive(draft.import.scale)||!Geometry.Finite(draft.import.orientation)||!Geometry.Finite(draft.import.origin)
                ||!Geometry.Finite(draft.import.projectedScale)||!Geometry.Positive(draft.import.projectedScale.x)||!Geometry.Positive(draft.import.projectedScale.y))return lastTexture;
            rendering=true;
            try{return RenderReady(source,draft,editor,showSource,orbit,size,dirty);}
            finally{rendering=false;}
        }
        private Texture RenderReady(GameObject source,PieceDraft draft,BomProfileEditor editor,bool showSource,Vector2 orbit,Vector2 size,bool dirty)
        {
            string key=showSource?"source:"+(source!=null?source.GetEntityId().ToString():"none")+":"+JsonUtility.ToJson(draft.import):"result";
            if(root==null||key!=geometryKey||(!showSource&&dirty))
            {
                Clear();geometryKey=key;HasSource=showSource&&source!=null;
                root=new GameObject("BOM model review") {hideFlags=HideFlags.HideAndDontSave};
                geometry=new GameObject("Model");geometry.transform.SetParent(root.transform,false);
                lineMaterial=new Material(Resources.Load<Shader>("BOMExtrusion")) {hideFlags=HideFlags.HideAndDontSave};owned.Add(lineMaterial);
                if(HasSource)BuildSource(source,draft.import);
                else if(Geometry.TryValidate(draft.profile,out _)&&Geometry.Positive(draft.thickness))
                {
                    var state=Extrusion.PreviewState(draft);var mesh=Extrusion.Build(state);owned.Add(mesh);
                    var appearance=draft.appearance.Clone();if(appearance.image!=null)appearance.approvedImagePng=ImportService.ImagePng(appearance.image);
                    var materials=Extrusion.Materials(new AppearanceRecord {face=appearance.face,side=appearance.side,cut=appearance.cut,
                        imagePng=appearance.approvedImagePng,wrapMode=appearance.wrapMode,filterMode=appearance.filterMode},out var texture);
                    owned.AddRange(materials);if(texture!=null)owned.Add(texture);
                    geometry.AddComponent<MeshFilter>().sharedMesh=mesh;geometry.AddComponent<MeshRenderer>().sharedMaterials=materials;
                }
                var renderers=geometry.GetComponentsInChildren<Renderer>();
                bounds=renderers.Length>0?renderers[0].bounds:new Bounds(Vector3.zero,Vector3.one);
                foreach(var renderer in renderers)bounds.Encapsulate(renderer.bounds);
                overlayDepth=bounds.min.z-Mathf.Max(.001f,bounds.size.magnitude*.002f);
                Utility.AddSingleGO(root);
            }
            root.transform.rotation=Quaternion.identity;
            if(overlay!=null)UnityEngine.Object.DestroyImmediate(overlay);
            overlay=new GameObject("Outline");overlay.transform.SetParent(root.transform,false);
            float width=Mathf.Max(.0001f,Mathf.Max(bounds.size.x,bounds.size.y)*.004f);
            int ring=0;
            foreach(var contour in draft.profile.Contours)
            {
                if(contour.points.Count<2){ring++;continue;}
                var colour=ring==editor?.SelectedRing?new Color(.3f,1,1):ring==0?Color.cyan:new Color(1,.65f,.15f);
                Line(contour.points,colour,width,true);
                if(editor!=null&&editor.SelectedRing==ring&&editor.HasSelection)
                {
                    var p=editor.SelectedPoint;float r=width*3;
                    Line(new[] {p+Vector2.left*r,p+Vector2.right*r},Color.white,width*2,false);
                    Line(new[] {p+Vector2.down*r,p+Vector2.up*r},Color.white,width*2,false);
                }
                ring++;
            }
            for(int i=0;i<draft.disconnectedProposal.Count;i++)Line(draft.disconnectedProposal[i].outer.points,
                editor?.SelectedRegion==i+1?Color.white:new Color(1,.35f,.3f),editor?.SelectedRegion==i+1?width*2:width,true);
            var frame=bounds;
            foreach(var p in draft.profile.Contours.SelectMany(c=>c.points).Where(Geometry.Finite))frame.Encapsulate(new Vector3(p.x,p.y,overlayDepth));
            root.transform.rotation=Quaternion.Euler(orbit.x,orbit.y,0);
            var corners=new[] {frame.min,new Vector3(frame.min.x,frame.min.y,frame.max.z),new Vector3(frame.min.x,frame.max.y,frame.min.z),
                new Vector3(frame.max.x,frame.min.y,frame.min.z),frame.max,new Vector3(frame.max.x,frame.max.y,frame.min.z),
                new Vector3(frame.max.x,frame.min.y,frame.max.z),new Vector3(frame.min.x,frame.max.y,frame.max.z)};
            var rotated=new Bounds(root.transform.TransformPoint(corners[0]),Vector3.zero);
            foreach(var corner in corners)rotated.Encapsulate(root.transform.TransformPoint(corner));
            Utility.camera.orthographic=true;
            Utility.camera.aspect=size.x/size.y;
            Utility.camera.orthographicSize=Mathf.Max(.01f,Mathf.Max(rotated.extents.y,rotated.extents.x/(size.x/size.y))*1.15f);
            Utility.camera.transform.position=rotated.center+Vector3.back*(rotated.extents.z+10);
            Utility.camera.transform.rotation=Quaternion.identity;
            Utility.camera.nearClipPlane=.01f;Utility.camera.farClipPlane=Mathf.Max(100,rotated.size.z+20);
            Utility.BeginPreview(new Rect(0,0,size.x,size.y),GUIStyle.none);
            try{Utility.Render(true);}
            finally{lastTexture=Utility.EndPreview();}
            return lastTexture;
        }
        private void BuildSource(GameObject source,ImportSettings settings)
        {
            foreach(var filter in source.GetComponentsInChildren<MeshFilter>(true))
            {
                if(filter.sharedMesh==null)continue;
                var sourceRenderer=filter.GetComponent<MeshRenderer>();if(sourceRenderer==null)continue;
                var transform=ImportService.MeshToSource(source,filter);
                var mesh=UnityEngine.Object.Instantiate(filter.sharedMesh);mesh.hideFlags=HideFlags.HideAndDontSave;
                mesh.vertices=mesh.vertices.Select(p=>ImportService.ToAuthoring(transform.MultiplyPoint3x4(p),settings)).ToArray();
                if(settings.plane==ProjectionPlane.XZ)
                    for(int s=0;s<mesh.subMeshCount;s++){var indices=mesh.GetTriangles(s);for(int i=0;i+2<indices.Length;i+=3){int t=indices[i+1];indices[i+1]=indices[i+2];indices[i+2]=t;}mesh.SetTriangles(indices,s);}
                mesh.RecalculateNormals();mesh.RecalculateBounds();owned.Add(mesh);
                var part=new GameObject(filter.name);part.transform.SetParent(geometry.transform,false);
                part.AddComponent<MeshFilter>().sharedMesh=mesh;part.AddComponent<MeshRenderer>().sharedMaterials=sourceRenderer.sharedMaterials;
            }
        }
        private void Line(IEnumerable<Vector2> points,Color colour,float width,bool loop)
        {
            var vertices=points.Where(Geometry.Finite).Select(p=>new Vector3(p.x,p.y,overlayDepth)).ToArray();if(vertices.Length<2)return;
            var go=new GameObject("Boundary");go.transform.SetParent(overlay.transform,false);
            var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=loop;line.positionCount=vertices.Length;line.SetPositions(vertices);
            line.widthMultiplier=width;line.sharedMaterial=lineMaterial;
            var properties=new MaterialPropertyBlock();properties.SetColor("_BaseColor",colour);line.SetPropertyBlock(properties);
        }
        public bool TryProfilePoint(Vector2 local,Vector2 size,out Vector2 point)
        {
            point=default;if(root==null||!Geometry.Finite(local)||!Geometry.Finite(size)||size.x<=0||size.y<=0)return false;
            var ray=Utility.camera.ViewportPointToRay(new Vector3(local.x/size.x,1-local.y/size.y,0));
            var plane=new Plane(root.transform.TransformDirection(Vector3.forward),root.transform.TransformPoint(new Vector3(0,0,overlayDepth)));
            if(!plane.Raycast(ray,out float distance))return false;
            point=root.transform.InverseTransformPoint(ray.GetPoint(distance));return true;
        }
        public float PickRadius(float pixels,float height) => Utility.camera.orthographicSize*2*pixels/Mathf.Max(1,height);
        private void Clear()
        {
            if(root!=null)UnityEngine.Object.DestroyImmediate(root);
            foreach(var item in owned)if(item!=null)UnityEngine.Object.DestroyImmediate(item);
            owned.Clear();root=null;overlay=null;geometry=null;
        }
        public void Dispose(){try{Clear();}finally{Utility.Cleanup();}}
    }
    public sealed class BomSilhouette : UnityEngine.UIElements.VisualElement
    {
        public BomSilhouette(Region region)
        {
            AddToClassList("bom-silhouette");pickingMode=UnityEngine.UIElements.PickingMode.Ignore;
            generateVisualContent+=context=>
            {
                var points=region?.Contours.SelectMany(c=>c.points).Where(Geometry.Finite).ToArray();if(points==null||points.Length<3)return;
                var min=new Vector2(points.Min(p=>p.x),points.Min(p=>p.y));var max=new Vector2(points.Max(p=>p.x),points.Max(p=>p.y));
                float scale=Mathf.Min((contentRect.width-12)/Mathf.Max(.01f,max.x-min.x),(contentRect.height-12)/Mathf.Max(.01f,max.y-min.y));
                var painter=context.painter2D;painter.strokeColor=Color.cyan;painter.lineWidth=1.5f;
                foreach(var contour in region.Contours)
                {
                    painter.BeginPath();int i=0;
                    foreach(var p in contour.points){var v=(p-(min+max)*.5f)*scale;var pixel=contentRect.center+new Vector2(v.x,-v.y);if(i++==0)painter.MoveTo(pixel);else painter.LineTo(pixel);}
                    painter.ClosePath();painter.Stroke();
                }
            };
        }
    }
}
