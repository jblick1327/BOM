using System.Linq;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BOM.Editor
{
    [CustomEditor(typeof(PieceDefinition))]
    public sealed class PieceDefinitionEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root=new VisualElement();var piece=(PieceDefinition)target;
            root.Add(new Label(piece.IsApproved?"Approved version "+piece.Version:"Unapproved definition"));
            root.Add(new Button(()=>BomAuthoringWindow.Review(piece)) {text="Review / correct / reimport"});
            if(piece.IsApproved)
            {
                var draft=piece.Draft();root.Add(new Label("Material: "+(draft.material!=null?draft.material.name:"missing")));
                root.Add(new Label("Destruction: "+draft.destruction.mode+"   Grab eligible: "+draft.grabEligible));
                root.Add(new Label("Gameplay area: "+Geometry.Area(draft.profile).ToString("0.#####")+" m²   Visual thickness: "+draft.thickness+" m"));
                root.Add(new Label("Source: "+draft.import.sourcePath));
                var report=AuthoringValidation.Piece(draft,piece);if(!report.IsValid) root.Add(new HelpBox(report.ToString(),HelpBoxMessageType.Error));
            }
            return root;
        }
    }
    [CustomEditor(typeof(PiecePlacement))]
    public sealed class PiecePlacementEditor : UnityEditor.Editor
    {
        private bool hidingScale;
        private bool previousHidden;
        private void RestoreHandles(){if(hidingScale){Tools.hidden=previousHidden;hidingScale=false;}}
        private void OnDisable()=>RestoreHandles();
        private void OnSceneGUI()
        {
            if(Application.isPlaying||Tools.current!=Tool.Scale){RestoreHandles();return;}
            if(!hidingScale)previousHidden=Tools.hidden;hidingScale=true;Tools.hidden=true;var placement=(PiecePlacement)target;var transform=placement.transform;
            EditorGUI.BeginChangeCheck();
            var scale=transform.localScale;var rotation=transform.rotation;float size=HandleUtility.GetHandleSize(transform.position);
            var oldColour=Handles.color;
            Handles.color=Handles.xAxisColor;float x=Handles.ScaleSlider(scale.x,transform.position,rotation*Vector3.right,rotation,size,EditorSnapSettings.scale);
            Handles.color=Handles.yAxisColor;float y=Handles.ScaleSlider(scale.y,transform.position,rotation*Vector3.up,rotation,size,EditorSnapSettings.scale);
            Handles.color=Handles.centerColor;float uniform=Handles.ScaleValueHandle(scale.x,transform.position,rotation,size*.15f,Handles.CubeHandleCap,EditorSnapSettings.scale);
            Handles.color=oldColour;
            if(!Mathf.Approximately(uniform,scale.x)){float ratio=Mathf.Max(.0001f,uniform)/Mathf.Max(.0001f,scale.x);x=scale.x*ratio;y=scale.y*ratio;}
            scale=new Vector3(x,y,1);
            if(EditorGUI.EndChangeCheck())
            {Undo.RecordObject(transform,"Scale BOM piece");transform.localScale=new Vector3(Mathf.Max(.0001f,scale.x),Mathf.Max(.0001f,scale.y),1);EditorUtility.SetDirty(transform);}
        }
        public override VisualElement CreateInspectorGUI()
        {
            var root=new VisualElement();InspectorElement.FillDefaultInspector(root,serializedObject,this);
            var placement=(PiecePlacement)target;var source=new Label();root.Add(source);
            void Update()
            {
                var material=placement.EffectiveMaterial;var destruction=placement.EffectiveDestruction;
                source.text="Effective material: "+(material!=null?material.name:"missing")+" ("+(placement.overrideMaterial?"placement override":"asset default")+")\n"
                    +"Destruction: "+(destruction!=null?destruction.mode.ToString():"missing")+" ("+(placement.overrideDestruction?"placement override":"asset default")+")\n"
                    +"Grab: "+placement.EffectiveGrab+" ("+(placement.overrideGrab?"placement override":"asset default")+")"
                    +(material!=null?"\nDensity: "+material.density+" kg/m²; resistance: "+material.blastResistance+" (selected material definition)":"");
                if(placement.piece!=null)
                {
                    if(LevelCompiler.TryPose(placement,out var pose,out var error))
                    {
                        var draft=placement.piece.Draft();var points=draft.profile.outer.points;
                        if(points.Count>0)source.text+=$"\n{(points.Max(p=>p.x)-points.Min(p=>p.x))*pose.scale.x:0.###} x {(points.Max(p=>p.y)-points.Min(p=>p.y))*pose.scale.y:0.###} m";
                        if(material!=null)source.text+=$" | {Geometry.Area(draft.profile)*pose.scale.x*pose.scale.y*material.density:0.###} kg";
                    }
                    else source.text+="\n"+error;
                }
            }
            void Reset(System.Action action,string name) {Undo.RecordObject(placement,name);action();EditorUtility.SetDirty(placement);serializedObject.Update();Update();}
            root.Add(new Button(()=>Reset(placement.ResetMaterial,"Reset material override")) {text="Reset material to asset default"});
            root.Add(new Button(()=>Reset(placement.ResetDestruction,"Reset destruction override")) {text="Reset destruction to asset default"});
            root.Add(new Button(()=>Reset(placement.ResetGrab,"Reset grab override")) {text="Reset grab to asset default"});
            root.Add(new Button(()=>{if(placement.piece!=null) BomAuthoringWindow.Review(placement.piece);}) {text="Review source piece asset"});
            root.schedule.Execute(Update).Every(300);Update();return root;
        }
    }
    [CustomEditor(typeof(ConnectionAuthoring))]
    public sealed class ConnectionAuthoringEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root=new VisualElement();InspectorElement.FillDefaultInspector(root,serializedObject,this);
            root.Add(new Button(()=>BomAuthoringWindow.Review((ConnectionAuthoring)target)) {text="Review paired region / alignment"});
            root.Add(new Button(()=>Undo.DestroyObjectImmediate(((ConnectionAuthoring)target).gameObject)) {text="Remove connection"});return root;
        }
    }
    [CustomEditor(typeof(LevelAuthoring))]
    public sealed class LevelAuthoringEditor : UnityEditor.Editor
    {
        public override VisualElement CreateInspectorGUI()
        {
            var root=new VisualElement();InspectorElement.FillDefaultInspector(root,serializedObject,this);var level=(LevelAuthoring)target;
            var report=new HelpBox("Validate the whole authored level before starting a test.",HelpBoxMessageType.Info);root.Add(report);
            root.Add(new Button(()=>{bool ok=LevelCompiler.TryCompile(level,out var resolved,out var validation);
                report.messageType=ok?HelpBoxMessageType.Info:HelpBoxMessageType.Error;report.text=ok?resolved.Read().bodies.Count+" resolved bodies; "+resolved.Read().bonds.Count+" connections.":validation.ToString();}) {text="Compile / validate full level"});
            root.Add(new Button(()=>{Selection.activeGameObject=level.gameObject;BomAuthoringWindow.Open();}) {text="Open authoring workspace"});
            root.Add(new Button(()=>{level.StartTest();report.text=level.LastReport;}) {text="Start local test (Play mode)"});
            root.Add(new Button(()=>{level.ResetTest();report.text=level.LastReport;}) {text="Reset to captured initial configuration"});return root;
        }
    }
}
