using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace BOM.Editor
{
    public sealed partial class BomAuthoringWindow : EditorWindow
    {
        [Flags] private enum AppearanceEdits {None=0,Face=1,Side=2,Cut=4,Image=8,U=16,V=32,Offset=64,Wrap=128,Filter=256,Thickness=512}
        [SerializeField] private int tab;
        [SerializeField] private PieceDefinition editing;
        [SerializeField] private GameObject source;
        [SerializeField] private PieceDraft draft;
        [SerializeField] private LevelAuthoring level;
        [SerializeField] private bool conversionDirty, draftEdited;
        [SerializeField] private bool appearanceProposed;
        [SerializeField] private AppearanceEdits appearanceEdits;
        [SerializeField] private ExplosionPreset presetToAdd;
        [SerializeField] private bool presetSelectionInitialized;
        private PieceDefinition placementAsset;
        private PiecePlacement endpointA,endpointB;
        private ConnectionAuthoring editingConnection;
        private ConnectionProposal connection;
        private Vector2 explosionOrigin;
        private int preset;
        private string snapshot,inference;
        private ScrollView content;
        private Label status;
        private Image image;
        private Label validation;
        private BomProfileEditor profileEditor;
        private VisualElement contourCoordinates;
        private Region coordinateProfile;
        private int[] coordinatePointCounts=Array.Empty<int>();
        private readonly System.Collections.Generic.List<(int ring,int vertex,Vector2Field field)> coordinateFields=new System.Collections.Generic.List<(int,int,Vector2Field)>();
        private readonly System.Collections.Generic.List<Button> coordinateActions=new System.Collections.Generic.List<Button>();
        private Button approveButton, continueButton, refreshButton;
        private bool beforeDragEdited;
        private bool windowEnabled,previewUpdating;
        private readonly System.Collections.Generic.List<(Button button, Func<bool> available)> actions = new System.Collections.Generic.List<(Button, Func<bool>)>();
        private PreviewRenderUtility preview;
        private bool previewDirty=true;
        private double nextPreview;
        private double nextControls;

        [MenuItem("BOM/Authoring workspace")]
        public static BomAuthoringWindow Open() => GetWindow<BomAuthoringWindow>("BOM authoring");
        public static void Review(PieceDefinition piece)
        {var window=Open();if(!window.AllowDiscardDraft()) return;window.editing=piece;window.source=ImportService.Source(piece);window.draft=piece.Draft();window.conversionDirty=false;window.draftEdited=false;window.appearanceProposed=true;window.appearanceEdits=AppearanceEdits.None;window.tab=0;window.previewDirty=true;window.Build();}
        public static void Review(ConnectionAuthoring bond)
        {Open().ReviewConnection(bond);}
        private void ReviewConnection(ConnectionAuthoring bond)
        {
            if(bond==null) {SetConnectionEndpoints(null,null);return;}
            editingConnection=bond;endpointA=bond.a;endpointB=bond.b;level=bond.GetComponentInParent<LevelAuthoring>();
            tab=2;connection=bond.a!=null&&bond.b!=null?new ConnectionProposal {a=bond.a,b=bond.b,
                start=bond.a.transform.TransformPoint(bond.a0),end=bond.a.transform.TransformPoint(bond.a1),strength=bond.strength,
                attachmentLoss=bond.attachmentLoss,minimumFraction=bond.minimumFraction}:null;Build();SceneView.RepaintAll();
        }
        private void OnEnable() {windowEnabled=true;EditorApplication.update+=Tick;SceneView.duringSceneGui+=SceneGUI;Undo.undoRedoPerformed+=UndoDraft;EditorApplication.playModeStateChanged+=PlayModeChanged;}
        private void OnDisable()
        {
            windowEnabled=false;EditorApplication.update-=Tick;SceneView.duringSceneGui-=SceneGUI;Undo.undoRedoPerformed-=UndoDraft;EditorApplication.playModeStateChanged-=PlayModeChanged;
            ClearPlacementPreview();var ownedPreview=modelPreview;modelPreview=null;preview=null;ownedPreview?.Dispose();
        }
        private void UndoDraft() {previewDirty=true;Build();}
        private void PlayModeChanged(PlayModeStateChange _) {ClearPlacementPreview();Build();}
        public override void SaveChanges()
        {if(!Approve())throw new InvalidOperationException("The piece draft was not saved. Correct the highlighted fields and save, or discard the edits.");base.SaveChanges();}
        public override void DiscardChanges()
        {draft=editing!=null?editing.Draft():null;conversionDirty=false;draftEdited=false;appearanceProposed=editing!=null;appearanceEdits=AppearanceEdits.None;previewDirty=true;base.DiscardChanges();Build();}
        private void BeginDraftEdit() {Undo.RegisterCompleteObjectUndo(this,"Edit BOM pending draft");}
        private bool AllowDiscardDraft() => !draftEdited || EditorUtility.DisplayDialog("Unsaved piece draft","Discard the pending edits? The approved piece will not change.","Discard draft","Keep editing");
        public void CreateGUI()
        {
            titleContent=new GUIContent("BOM authoring");minSize=new Vector2(560,700);rootVisualElement.Clear();
            var tree=AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/BOM/Editor/UI/BomAuthoring.uxml");
            if(tree==null) {rootVisualElement.Add(new Label("Restore the BOM authoring UXML asset."));return;}
            tree.CloneTree(rootVisualElement);content=rootVisualElement.Q<ScrollView>("content");status=rootVisualElement.Q<Label>("status");
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(e=>rootVisualElement.Q<VisualElement>("workbench")?.EnableInClassList("bom-narrow",e.newRect.width<900));
            var toolbar=rootVisualElement.Q<VisualElement>("toolbar");
            string[] names={"Import","Place","Connect","Playtest"};
            for(int i=0;i<names.Length;i++) {int selected=i;var button=new Button(()=>{ClearPlacementPreview();tab=selected;Build();}) {text=names[i],name="step"+i};toolbar.Add(button);}
            Build();
        }
        private VisualElement Section(string title)
        {
            var section=new VisualElement();section.AddToClassList("bom-section");
            var label=new Label(title);label.AddToClassList("bom-section-title");section.Add(label);content.Add(section);return section;
        }
        private void Help(VisualElement parent,string text) {var label=new Label(text);label.AddToClassList("bom-help");parent.Add(label);}
        private Button Button(VisualElement parent,string text,Action action,bool primary=false,Func<bool> available=null)
        {var button=new Button(()=>{if(available==null||available()) action();}) {text=text};if(primary) button.AddToClassList("bom-primary");parent.Add(button);if(available!=null){actions.Add((button,available));button.SetEnabled(available());}return button;}
        private void ObjectField<T>(VisualElement parent,string label,T value,Action<T> setter,bool scene=false) where T:UnityEngine.Object
        {var field=new ObjectField(label) {objectType=typeof(T),allowSceneObjects=scene,value=value};field.RegisterValueChangedCallback(e=>setter(e.newValue as T));parent.Add(field);}
        private void Float(VisualElement parent,string label,float value,Action<float> setter)
        {var field=new FloatField(label) {value=value};field.RegisterValueChangedCallback(e=>{if(tab==0&&draft!=null)BeginDraftEdit();setter(e.newValue);Changed();});parent.Add(field);}
        private void Vector(VisualElement parent,string label,Vector2 value,Action<Vector2> setter)
        {var field=new Vector2Field(label) {value=value};field.RegisterValueChangedCallback(e=>{if(tab==0&&draft!=null)BeginDraftEdit();setter(e.newValue);Changed();});parent.Add(field);}
        private void Colour(VisualElement parent,string label,Color value,Action<Color> setter)
        {var field=new ColorField(label) {value=value};field.RegisterValueChangedCallback(e=>{if(tab==0&&draft!=null)BeginDraftEdit();setter(e.newValue);Changed();});parent.Add(field);}
        private void Toggle(VisualElement parent,string label,bool value,Action<bool> setter)
        {var field=new Toggle(label) {value=value};field.RegisterValueChangedCallback(e=>{if(tab==0&&draft!=null)BeginDraftEdit();setter(e.newValue);Changed();});parent.Add(field);}
        private void Message(string message) {if(status!=null) status.text=message;}
        private void Changed() {if(tab==0&&draft!=null)draftEdited=true;hasUnsavedChanges=draftEdited;previewDirty=true;profileEditor?.RefreshSelection();SyncContourUI();profileEditor?.MarkDirtyRepaint();UpdateValidation();RefreshActions();SceneView.RepaintAll();}
        private void RefreshActions() {foreach(var action in actions) action.button.SetEnabled(action.available());}
        private void Build()
        {
            if(content==null) return;actions.Clear();coordinateActions.Clear();coordinateFields.Clear();contourCoordinates=null;coordinateProfile=null;coordinatePointCounts=Array.Empty<int>();content.Clear();image=null;validation=null;profileEditor=null;approveButton=null;continueButton=null;refreshButton=null;
            widthField=null;heightField=null;dimensionsLabel=null;draftState=null;
            rootVisualElement.Q<VisualElement>("visuals")?.Clear();rootVisualElement.Q<VisualElement>("footer")?.Clear();
            rootVisualElement.Q<VisualElement>("visuals")?.EnableInClassList("bom-hidden",tab!=0||draft==null);
            rootVisualElement.Q<VisualElement>("footer")?.EnableInClassList("bom-hidden",tab!=0||draft==null);
            rootVisualElement.Q<VisualElement>("visuals")?.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
            rootVisualElement.Q<VisualElement>("footer")?.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
            hasUnsavedChanges=draftEdited;saveChangesMessage="Save this piece or discard the pending edits.";
            for(int i=0;i<4;i++) rootVisualElement.Q<Button>("step"+i)?.EnableInClassList("bom-tab-active",tab==i);
            if(tab==0) ImportUI();else if(tab==1) PlacementUI();else if(tab==2) ConnectionUI();else TestUI();
            content.SetEnabled(tab==3||!EditorApplication.isPlayingOrWillChangePlaymode);
            if(tab!=3&&EditorApplication.isPlayingOrWillChangePlaymode)Message("Stop Play mode to edit.");
            RefreshActions();
        }
        private void ImportUI() => BuildImportWorkspace();
        private bool CanProject => !Application.isPlaying&&source!=null&&string.Equals(Path.GetExtension(AssetDatabase.GetAssetPath(source)),".fbx",StringComparison.OrdinalIgnoreCase);
        private bool CanApprove => !Application.isPlaying&&!conversionDirty&&draft!=null&&AuthoringValidation.Piece(draft,editing).IsValid;
        private void SelectSource(GameObject value)
        {
            if(!AllowDiscardDraft()){Build();return;}
            BeginDraftEdit();source=value;editing=null;draft=null;conversionDirty=false;draftEdited=false;appearanceProposed=false;appearanceEdits=AppearanceEdits.None;inference=null;previewDirty=true;
            if(CanProject)Generate(true);else {Build();Message(value==null?"Select an FBX model to begin.":"Choose a Blender-exported FBX asset, not a scene object or another file format.");}
        }
        private void ConversionChanged()
        {
            conversionDirty=true;Changed();
            if(CanProject&&!draft.import.manuallyCorrected)Generate(true,false);
            else {UpdateValidation();Message("Rebuild outline to apply the new view.");}
        }
        private void AddHole()
        {
            if(!Geometry.TryValidate(draft.profile,out _))return;
            var candidates=Geometry.Triangles(draft.profile).OrderByDescending(t=>Math.Abs(Geometry.Cross(t[1]-t[0],t[2]-t[0])));
            foreach(var triangle in candidates)
            {
                var centre=(triangle[0]+triangle[1]+triangle[2])/3;
                var candidate=draft.profile.Clone();candidate.holes.Add(new Contour(triangle.Select(p=>Vector2.Lerp(centre,p,.3f))));
                if(!Geometry.TryValidate(candidate,out _))continue;
                BeginDraftEdit();draft.profile=candidate;draft.import.manuallyCorrected=true;Changed();Build();profileEditor?.SelectRing(draft.profile.holes.Count);return;
            }
            Message("This outline is too small to add a valid hole. Enlarge it or use precise contour editing.");
        }
        private void FitImage()
        {
            var points=draft.profile.outer.points;float x=points.Min(p=>p.x),y=points.Min(p=>p.y);
            float width=points.Max(p=>p.x)-x,height=points.Max(p=>p.y)-y;if(width<=0||height<=0)return;
            BeginDraftEdit();draft.appearance.u=new Vector2(1/width,0);draft.appearance.v=new Vector2(0,1/height);draft.appearance.offset=new Vector2(-x/width,-y/height);appearanceEdits|=AppearanceEdits.U|AppearanceEdits.V|AppearanceEdits.Offset;Changed();Build();
        }
        private Contour CurrentContour(int ring) => draft?.profile==null||ring<0||ring>draft.profile.holes.Count?null:ring==0?draft.profile.outer:draft.profile.holes[ring-1];
        private Contour EditableCoordinateContour(int ring)
        {
            var contour=CurrentContour(ring);
            return profileEditor?.IsDragging==true||coordinateProfile!=draft?.profile||ring<0||ring>=coordinatePointCounts.Length||contour==null||coordinatePointCounts[ring]!=contour.points.Count?null:contour;
        }
        private Contour EditableCoordinate(Vector2Field field,int ring,int index)
        {
            var contour=EditableCoordinateContour(ring);
            return contour==null||index<0||index>=contour.points.Count||!coordinateFields.Any(c=>c.field==field)?null:contour;
        }
        private void SyncContourUI()
        {
            if(contourCoordinates==null||draft?.profile==null)return;
            var contours=draft.profile.Contours.ToArray();var counts=contours.Select(c=>c.points.Count).ToArray();
            bool topology=coordinateProfile!=draft.profile||!coordinatePointCounts.SequenceEqual(counts);
            if(topology&&profileEditor?.IsDragging!=true)
            {
                var foldouts=contourCoordinates.Query<Foldout>().ToList().ToDictionary(f=>f.name,f=>f.value);
                actions.RemoveAll(a=>coordinateActions.Contains(a.button));coordinateActions.Clear();coordinateFields.Clear();contourCoordinates.Clear();
                coordinateProfile=draft.profile;coordinatePointCounts=counts;
                for(int ring=0;ring<contours.Length;ring++)ContourUI(ring,contours[ring].points.Count,foldouts.TryGetValue("contour"+ring,out var expanded)&&expanded);
                topology=false;
            }
            foreach(var binding in coordinateFields)
            {
                var contour=CurrentContour(binding.ring);bool valid=contour!=null&&binding.vertex<contour.points.Count;
                binding.field.SetEnabled(valid&&!topology&&profileEditor?.IsDragging!=true);
                if(valid)binding.field.SetValueWithoutNotify(contour.points[binding.vertex]);
            }
        }
        private void ContourUI(int ring,int count,bool expanded)
        {
            var fold=new Foldout {name="contour"+ring,text=ring==0?"Outer contour coordinates":"Hole "+ring+" coordinates",value=expanded};contourCoordinates.Add(fold);
            for(int i=0;i<count;i++)
            {
                int index=i;var row=new VisualElement();row.AddToClassList("bom-row");
                var field=new Vector2Field("Vertex "+i) {value=CurrentContour(ring).points[i]};field.AddToClassList("bom-flex");coordinateFields.Add((ring,index,field));
                field.RegisterValueChangedCallback(e=>{var contour=EditableCoordinate(field,ring,index);if(contour==null)return;BeginDraftEdit();contour.points[index]=e.newValue;contour.cutEdges.Clear();draft.import.manuallyCorrected=true;Changed();});row.Add(field);
                coordinateActions.Add(Button(row,"Remove",()=>{var contour=EditableCoordinate(field,ring,index);if(contour==null||contour.points.Count<=3)return;BeginDraftEdit();contour.points.RemoveAt(index);profileEditor?.PointRemoved(ring,index);contour.cutEdges.Clear();draft.import.manuallyCorrected=true;Changed();},false,()=>EditableCoordinate(field,ring,index)?.points.Count>3));fold.Add(row);
            }
            coordinateActions.Add(Button(fold,"Add vertex",()=>{var contour=EditableCoordinateContour(ring);if(contour==null)return;BeginDraftEdit();contour.points.Add(contour.points.Count<2?Vector2.right*contour.points.Count:(contour.points[contour.points.Count-1]+contour.points[0])*.5f);
                contour.cutEdges.Clear();draft.import.manuallyCorrected=true;Changed();},false,()=>EditableCoordinateContour(ring)!=null));
        }
        private void PreserveExplicitAppearance(PieceDraft proposed)
        {
            bool Edited(AppearanceEdits flag) => (appearanceEdits&flag)!=0;
            var appearance=proposed.appearance;
            if(Edited(AppearanceEdits.Face))appearance.face=draft.appearance.face;
            if(Edited(AppearanceEdits.Side))appearance.side=draft.appearance.side;
            if(Edited(AppearanceEdits.Cut))appearance.cut=draft.appearance.cut;
            if(Edited(AppearanceEdits.Image)){appearance.image=draft.appearance.image;appearance.approvedImagePng=draft.appearance.approvedImagePng;}
            if(Edited(AppearanceEdits.U))appearance.u=draft.appearance.u;
            if(Edited(AppearanceEdits.V))appearance.v=draft.appearance.v;
            if(Edited(AppearanceEdits.Offset))appearance.offset=draft.appearance.offset;
            if(Edited(AppearanceEdits.Wrap))appearance.wrapMode=draft.appearance.wrapMode;
            if(Edited(AppearanceEdits.Filter))appearance.filterMode=draft.appearance.filterMode;
            if(Edited(AppearanceEdits.Thickness))proposed.thickness=draft.thickness;
        }
        private void Generate(bool regenerate,bool confirmManual=true)
        {
            if(!CanProject){Message("Stop Play mode and select a Blender-exported FBX asset to project.");return;}
            if(regenerate&&confirmManual&&draft?.import.manuallyCorrected==true&&!EditorUtility.DisplayDialog("Replace corrected outline?","Projection replaces the pending contour edits with geometry from the source model. The approved asset stays unchanged until approval.","Replace outline","Keep edits"))return;
            if(!regenerate&&!AllowDiscardDraft())return;
            var proposal=ImportService.Propose(source,editing,regenerate,draft?.import);
            if(draft==null&&editing==null){RestoreSelections(proposal.draft);appearanceMaterial=ImportService.SourceMaterial(source);showSource=true;previewOrbit=Vector2.zero;}
            if(proposal.report.issues.Any(i=>i.field=="Source"||i.field=="Source format"||i.field=="Projection"))
            {if(draft==null){draft=proposal.draft;conversionDirty=true;draftEdited=true;Build();}ShowReport(proposal.report);return;}
            if(regenerate&&draft!=null)
            {
                proposal.draft.material=draft.material;proposal.draft.destruction=draft.destruction.Clone();proposal.draft.grabEligible=draft.grabEligible;
                if(appearanceProposed||editing!=null){proposal.draft.appearance=draft.appearance.Clone();proposal.draft.thickness=draft.thickness;}
                else PreserveExplicitAppearance(proposal.draft);
            }
            BeginDraftEdit();draft=proposal.draft;conversionDirty=false;draftEdited=true;appearanceProposed=true;if(!regenerate)appearanceEdits=AppearanceEdits.None;inference=proposal.inference;previewDirty=true;Build();Message("");
        }
        private void UpdateValidation()
        {
            if(validation==null||draft==null)return;
            UpdateDimensions();
            var report=AuthoringValidation.Piece(draft,editing);
            Geometry.TryValidate(draft.profile,out var geometryError);profileEditor?.SetGeometryError(geometryError);
            string hint=conversionDirty?"Rebuild outline to apply the new view.":geometryError!=null?geometryError:
                draft.disconnectedProposal.Count>0?"Choose a region to keep.":draft.material==null?"Choose a physics material.":
                draft.destruction.mode==DestructionMode.Subtract&&draft.destruction.response==null?"Choose a destruction response.":
                !report.IsValid?report.issues[0].explanation:"";
            validation.text=hint;validation.tooltip=report.ToString();
            validation.EnableInClassList("bom-error",geometryError!=null&&draft.profile.outer.points.Count>=3);
            if(approveButton!=null)approveButton.tooltip=hint;if(continueButton!=null)continueButton.tooltip=hint;
        }
        private bool Approve(string newPath=null)
        {
            if(conversionDirty||Application.isPlaying){Message("Stop Play mode and update pending projection settings before approval.");return false;}
            var report=AuthoringValidation.Piece(draft,editing);if(!report.IsValid) {ShowReport(report);return false;}
            PieceDefinition target=editing;string path=null;
            if(target==null)
            {
                path=newPath??EditorUtility.SaveFilePanelInProject("Save BOM piece",source!=null?source.name:"NewPiece","asset","Save the reusable piece.",EditorPrefs.GetString(PreferenceRoot+"Folder","Assets"));if(string.IsNullOrEmpty(path))return false;
                if(!BomAuthoringAssets.NewPath(path,out var error)){Message(error);return false;}
                target=CreateInstance<PieceDefinition>();
            }
            if(!ImportService.Approve(target,draft,out report)) {if(editing==null) DestroyImmediate(target);ShowReport(report);return false;}
            if(path!=null)AssetDatabase.CreateAsset(target,path);AssetDatabase.SaveAssetIfDirty(target);editing=target;draft=target.Draft();placementAsset=target;
            conversionDirty=false;draftEdited=false;appearanceProposed=true;appearanceEdits=AppearanceEdits.None;previewDirty=true;RememberSelections();Build();Message("Saved "+target.name+".");return true;
        }
        private void CreateMaterial(string path=null)
        {var asset=BomAuthoringAssets.Create<MaterialDefinition>(path,"NewMaterial",out var error);if(asset==null){if(error!=null)Message(error);return;}BeginDraftEdit();draft.material=asset;Changed();Build();Message("Material created.");}
        private void CreateResponse(string path=null)
        {var asset=BomAuthoringAssets.Create<DestructionResponse>(path,"NewDestructionResponse",out var error);if(asset==null){if(error!=null)Message(error);return;}BeginDraftEdit();draft.destruction.response=asset;Changed();Build();Message("Response created.");}
        private void DuplicateMaterial(string path=null)
        {var copy=BomAuthoringAssets.Duplicate(draft.material,path,out var error);if(copy==null){if(error!=null)Message(error);return;}BeginDraftEdit();draft.material=copy;Changed();Build();Message("Using "+copy.name+". Save piece to keep this selection.");}
        private void DuplicateResponse(string path=null)
        {var copy=BomAuthoringAssets.Duplicate(draft.destruction.response,path,out var error);if(copy==null){if(error!=null)Message(error);return;}BeginDraftEdit();draft.destruction.response=copy;Changed();Build();Message("Using "+copy.name+". Save piece to keep this selection.");}
        private void LevelField(VisualElement parent)
        {
            if(level==null&&Selection.activeGameObject!=null) level=Selection.activeGameObject.GetComponentInParent<LevelAuthoring>();
            ObjectField<LevelAuthoring>(parent,"Level",level,v=>{level=v;presetToAdd=null;presetSelectionInitialized=false;SetConnectionEndpoints(null,null);},true);
        }
        private void PlacementUI()
        {
            var section=Section("Choose a level and place the approved piece");LevelField(section);

            Button(section,"Create level authoring group",CreateLevel);
            ObjectField<PieceDefinition>(section,"Piece",placementAsset,v=>{ClearPlacementPreview();placementAsset=v;RefreshActions();});
            Button(section,"Place in scene",StartPlacementPreview,true,()=>level!=null&&placementAsset!=null&&placementAsset.IsApproved&&!Application.isPlaying);
            if(placementAsset==null)Help(section,"Choose a saved piece.");
            if(level==null)Help(section,"Choose or create a level.");
            Button(section,"Place at view centre",PlacePiece,true,()=>level!=null&&placementAsset!=null&&placementAsset.IsApproved&&!Application.isPlaying);
            section.tooltip="Each placement is an independent body. Save groups as Unity prefabs.";
            var supports=Section("Choose fixed supports (optional)");

            Button(supports,"Make selected pieces fixed supports",()=>SetSupports(true),false,()=>SelectedPlacements().Length>0);
            Button(supports,"Make selected pieces movable",()=>SetSupports(false),false,()=>SelectedPlacements().Length>0);
            Button(section,"Validate level",Compile,false,()=>level!=null);
            Button(section,"Continue to connections",()=>{tab=2;Build();},true,()=>level!=null&&level.GetComponentsInChildren<PiecePlacement>(true).Length>0);
        }
        private void CreateLevel()
        {var go=new GameObject("BOM Level");Undo.RegisterCreatedObjectUndo(go,"Create BOM level");level=go.AddComponent<LevelAuthoring>();var starter=StarterPresets.Get<ExplosionPreset>("Medium");if(starter!=null)level.testPresets.Add(starter);presetToAdd=null;presetSelectionInitialized=false;Selection.activeGameObject=go;Build();Message("Level created.");}
        private void PlacePiece()
        {
            var view=SceneView.lastActiveSceneView;PlacePieceAt(view!=null?(Vector2)view.pivot:Vector2.zero);
        }
        private PiecePlacement[] SelectedPlacements() => level==null?Array.Empty<PiecePlacement>():Selection.gameObjects.Select(g=>g.GetComponent<PiecePlacement>()).Where(p=>p!=null&&p.transform.IsChildOf(level.transform)).ToArray();
        private void SetSupports(bool fixedSupport)
        {
            if(level==null||Application.isPlaying)return;
            Undo.RecordObject(level,"Change BOM fixed supports");if(level.foundations==null)level.foundations=new System.Collections.Generic.List<PiecePlacement>();
            foreach(var placement in SelectedPlacements()){if(fixedSupport&&!level.foundations.Contains(placement))level.foundations.Add(placement);else if(!fixedSupport)level.foundations.Remove(placement);}
            EditorUtility.SetDirty(level);EditorSceneManager.MarkSceneDirty(level.gameObject.scene);Message("Fixed supports: "+level.foundations.Count+". Other pieces remain movable.");
        }
        private void ConnectionUI()
        {
            var inputs=Section("Select endpoints");LevelField(inputs);
            if(editingConnection!=null)Help(inputs,"Editing "+editingConnection.name);
            ObjectField<PiecePlacement>(inputs,"Piece A",endpointA,v=>SetConnectionEndpoints(v,endpointB),true);
            ObjectField<PiecePlacement>(inputs,"Piece B",endpointB,v=>SetConnectionEndpoints(endpointA,v),true);
            Button(inputs,"New connection",()=>SetConnectionEndpoints(endpointA,endpointB));
            Button(inputs,"Use two selected pieces",()=>{var selected=Selection.gameObjects.Select(g=>g.GetComponent<PiecePlacement>()).Where(p=>p!=null).ToArray();if(selected.Length==2) SetConnectionEndpoints(selected[0],selected[1]);else Message("Select exactly two piece placements.");});
            inputs.tooltip="Select two pieces with a shared edge. Preview alignment to close a gap.";
            Func<bool> hasPair=()=>level!=null&&endpointA!=null&&endpointB!=null&&endpointA!=endpointB;
            Button(inputs,"Propose shared-boundary bond",()=>ProposeConnection(false),false,hasPair);Button(inputs,"Preview alignment of parallel boundaries",()=>ProposeConnection(true),false,hasPair);
            Button(inputs,"Continue to playtest",()=>{tab=3;Build();},true,()=>level!=null);
            if(connection==null) return;
            var proposal=connection;
            var review=Section("Review paired attachment region");Vector(review,"World start",proposal.start,v=>proposal.start=v);Vector(review,"World end",proposal.end,v=>proposal.end=v);
            Float(review,"Strength per metre",proposal.strength,v=>proposal.strength=v);Toggle(review,"Attachment-loss policy",proposal.attachmentLoss,v=>proposal.attachmentLoss=v);
            Float(review,"Minimum surviving fraction (0–1)",proposal.minimumFraction,v=>proposal.minimumFraction=v);
            if(proposal.translationB!=Vector2.zero)Help(review,"Alignment offset: "+proposal.translationB);
            Button(review,"Confirm connection",ConfirmConnection,true);
            Button(review,"Cancel connection proposal",()=>{SetConnectionEndpoints(endpointA,endpointB);Message("Cancelled; transforms and saved connections are unchanged.");});
            if(editingConnection!=null) Button(review,"Remove saved connection",()=>{Undo.DestroyObjectImmediate(editingConnection.gameObject);editingConnection=null;connection=null;Build();});
        }
        private void SetConnectionEndpoints(PiecePlacement a,PiecePlacement b)
        {endpointA=a;endpointB=b;editingConnection=null;connection=null;Build();SceneView.RepaintAll();}
        private void ProposeConnection(bool snap)
        {
            bool valid=ConnectionTools.TryPropose(endpointA,endpointB,snap,out connection,out var error);
            Build();SceneView.RepaintAll();Message(valid?(snap?"Alignment is previewed. Confirm to apply it; cancel keeps both placements intact.":"Review the highlighted attachment extent and strength."):error);
        }
        private void ConfirmConnection()
        {
            if(level==null||connection==null) {Message("Select a level and review a connection first.");return;}
            if(!ConnectionTools.TryValidateProposal(connection,out var error)) {Message(error);return;}
            if(!connection.a.transform.IsChildOf(level.transform)||!connection.b.transform.IsChildOf(level.transform)
                ||(editingConnection!=null&&!editingConnection.transform.IsChildOf(level.transform)))
            {Message("Both endpoints and an edited bond must belong to the selected level.");return;}
            var previousPosition=connection.b.transform.position;Undo.RecordObject(connection.b.transform,"Confirm BOM alignment");
            connection.b.transform.position+=new Vector3(connection.translationB.x,connection.translationB.y,0);
            var savedOffset=connection.translationB;connection.translationB=Vector2.zero;
            var target=editingConnection;
            if(target==null) {var go=new GameObject("BOM Bond");go.transform.SetParent(level.transform,false);Undo.RegisterCreatedObjectUndo(go,"Create BOM bond");target=go.AddComponent<ConnectionAuthoring>();}
            Undo.RecordObject(target,"Confirm BOM connection");
            if(!ConnectionTools.TrySet(target,connection,out error))
            {connection.b.transform.position=previousPosition;connection.translationB=savedOffset;if(editingConnection==null) DestroyImmediate(target.gameObject);Message(error);return;}
            EditorUtility.SetDirty(target);EditorSceneManager.MarkSceneDirty(level.gameObject.scene);Selection.activeGameObject=target.gameObject;
            editingConnection=null;connection=null;Build();SceneView.RepaintAll();Message("Connection saved with both local regions and its intended rigid fit. Ready to create another bond.");
        }
        private void Compile()
        {if(LevelCompiler.TryCompile(level,out var resolved,out var report)) {var state=resolved.Read();Message("Valid level: "+state.bodies.Count+" bodies, "+state.bonds.Count+" connections, "+state.bodies.Sum(b=>b.region.Contours.Sum(c=>c.points.Count))+" boundary vertices.");}else ShowReport(report);}
        private void ShowReport(ValidationReport report)
        {
            Message(report.ToString());var section=Section("Corrections required");
            foreach(var issue in report.issues)
            {var row=new Button(()=>{if(issue.target!=null){Selection.activeObject=issue.target;EditorGUIUtility.PingObject(issue.target);}}) {text=issue.ToString()};row.AddToClassList("bom-error");section.Add(row);}
        }
        private void TestUI()
        {
            var inputs=Section("Choose a level and set up a local test");LevelField(inputs);
            inputs.tooltip="Playtests capture a copy of the level. Restart to include authoring changes.";
            var setup=Section("Shared explosion presets");
            setup.tooltip="Radius, reach budget and impulse are captured at test start.";
            setup.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
            if(!presetSelectionInitialized){presetToAdd=presetToAdd??level?.testPresets?.FirstOrDefault(p=>p!=null)??StarterPresets.Get<ExplosionPreset>("Medium");presetSelectionInitialized=true;}
            ObjectField<ExplosionPreset>(setup,"Preset",presetToAdd,v=>{presetToAdd=v;Build();});
            DefinitionActions(setup,"blast",presetToAdd,v=>{presetToAdd=v;Build();},()=>DuplicatePreset(),"New preset",()=>CreatePreset(),level!=null);
            InlineProperties(setup,presetToAdd,"radius","reachBudget","impulseSpeed");
            Button(setup,"Add selected preset to level",()=>{AddPreset(presetToAdd);Build();},false,()=>!Application.isPlaying&&level!=null&&presetToAdd!=null&&presetToAdd.TryValidate(out _)&&!(level.testPresets?.Contains(presetToAdd)??false));
            if(level?.testPresets==null||level.testPresets.Count==0)Help(setup,"Add an explosion preset.");
            else foreach(var configured in level.testPresets)
            {
                var row=new VisualElement();row.AddToClassList("bom-row");setup.Add(row);var name=new Label(configured!=null?configured.name:"Missing preset");name.AddToClassList("bom-flex");row.Add(name);
                Button(row,"Select",()=>{presetToAdd=configured;Build();},false,()=>configured!=null);
                Button(row,"Remove",()=>{Undo.RecordObject(level,"Remove BOM explosion preset");level.testPresets.Remove(configured);EditorUtility.SetDirty(level);EditorSceneManager.MarkSceneDirty(level.gameObject.scene);Build();});
            }
            Button(setup,"Refresh preset list",Build);
            Button(inputs,"Validate full level",Compile,false,()=>level!=null);
            Func<bool> canStart=()=>level!=null&&level.GetComponentsInChildren<PiecePlacement>(true).Length>0&&level.testPresets!=null&&level.testPresets.Count>0&&level.testPresets.All(p=>p!=null&&p.TryValidate(out _));
            Button(inputs,"Enter Play mode",()=>{if(LevelCompiler.TryCompile(level,out _,out var report)) EditorApplication.isPlaying=true;else ShowReport(report);},true,()=>!EditorApplication.isPlayingOrWillChangePlaymode&&canStart());
            Button(inputs,"Start / capture a new initial configuration",()=>{level.StartTest();Build();Message(level.LastReport);},false,()=>Application.isPlaying&&canStart());
            Button(inputs,"Reset to last captured start",()=>{level.ResetTest();Build();Message(level.LastReport);},false,()=>Application.isPlaying&&level?.Session!=null);
            var explosion=Section("Test explosion");Vector(explosion,"Origin in world XY",explosionOrigin,v=>explosionOrigin=v);
            var captured=level?.Session?.ReadCommitted()?.configuration.testPresets;
            var choices=captured!=null?captured.Select((p,i)=>(i+1)+". "+p.label+" (radius "+p.radius+" m)").ToList():level?.testPresets?.Select((p,i)=>(i+1)+". "+(p!=null?p.name:"Missing preset")).ToList()??new System.Collections.Generic.List<string>();
            var presetField=new DropdownField {label=captured!=null?"Captured explosion preset":"Explosion preset (captured at start)",choices=choices,name="explosionPreset"};
            if(choices.Count>0){preset=Mathf.Clamp(preset,0,choices.Count-1);presetField.index=preset;}else presetField.SetValueWithoutNotify("No presets available");
            presetField.SetEnabled(choices.Count>0);presetField.RegisterValueChangedCallback(_=>{preset=presetField.index;RefreshActions();});explosion.Add(presetField);
            Help(explosion,"Shift-click exterior air in Scene view to explode.");
            explosion.tooltip="Origins inside solids or enclosed cavities are unsupported.";
            Button(explosion,"Run explosion",()=>{level.Explode(explosionOrigin,preset);Message(level.LastReport);},true,()=>Application.isPlaying&&level?.Session!=null&&captured!=null&&preset>=0&&preset<captured.Count&&Geometry.Finite(explosionOrigin));
            var recovery=new Foldout {text="Advanced: current-state snapshots and reference fixtures",value=false};content.Add(recovery);
            Button(recovery,"Capture committed world",()=>{snapshot=level.Session.Capture(true);Build();Message("Captured one committed state, including image and definition data.");},false,()=>Application.isPlaying&&level?.Session!=null);
            var text=new TextField("Snapshot JSON") {multiline=true,value=snapshot??""};text.AddToClassList("bom-snapshot");text.RegisterValueChangedCallback(e=>snapshot=e.newValue);recovery.Add(text);
            Button(recovery,"Reconstruct from snapshot",()=>{bool ok=level.Session.TryRecover(snapshot,out var error);if(ok)Build();Message(ok?"Current world reconstructed from supplied state and definitions.":error);},false,()=>Application.isPlaying&&level?.Session!=null&&!string.IsNullOrWhiteSpace(snapshot));
            Button(recovery,"Save snapshot JSON",()=>{string path=EditorUtility.SaveFilePanel("Save BOM capture","","BOM-current-world.json","json");if(!string.IsNullOrEmpty(path)) File.WriteAllText(path,snapshot);},false,()=>!string.IsNullOrWhiteSpace(snapshot));
            Button(recovery,"Load snapshot JSON",()=>{string path=EditorUtility.OpenFilePanel("Load BOM capture","","json");if(!string.IsNullOrEmpty(path)){snapshot=File.ReadAllText(path);Build();}});
            Button(recovery,"Create saved reference fixtures",()=>{if(EditorUtility.DisplayDialog("Regenerate reference fixtures?","This overwrites the generated examples under Assets/BOM/Fixtures. It is not required for normal authoring.","Regenerate examples","Cancel")){FixtureBuilder.Create();Message("Reference fixtures regenerated.");}},false,()=>!Application.isPlaying);
        }
        private void AddPreset(ExplosionPreset asset)
        {
            if(level==null||Application.isPlaying||asset==null||!asset.TryValidate(out _))return;
            Undo.RecordObject(level,"Add BOM shared explosion preset");if(level.testPresets==null)level.testPresets=new System.Collections.Generic.List<ExplosionPreset>();
            if(!level.testPresets.Contains(asset))level.testPresets.Add(asset);presetToAdd=asset;
            EditorUtility.SetDirty(level);EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
        }
        private void CreatePreset(string path=null)
        {if(level==null||Application.isPlaying)return;var asset=BomAuthoringAssets.Create<ExplosionPreset>(path,"NewExplosionPreset",out var error);if(asset==null){if(error!=null)Message(error);return;}AddPreset(asset);Build();Message("Preset created and added. Edit its values here.");}
        private void DuplicatePreset(string path=null)
        {
            if(level==null||Application.isPlaying)return;var sourcePreset=presetToAdd;var copy=BomAuthoringAssets.Duplicate(sourcePreset,path,out var error);
            if(copy==null){if(error!=null)Message(error);return;}
            Undo.RecordObject(this,"Choose duplicated BOM explosion preset");
            int index=level.testPresets?.IndexOf(sourcePreset)??-1;
            if(index>=0){Undo.RecordObject(level,"Use duplicated BOM explosion preset");level.testPresets[index]=copy;presetToAdd=copy;EditorUtility.SetDirty(level);EditorSceneManager.MarkSceneDirty(level.gameObject.scene);}
            else AddPreset(copy);
            Build();Message("Level uses "+copy.name+". The original preset is unchanged.");
        }
        private void Tick() => RenderImportPreview();
        private void SceneGUI(SceneView view)
        {
            if(PlacementSceneGUI(view))return;
            if(connection!=null)
            {
                Handles.color=Color.yellow;Handles.DrawAAPolyLine(6,new Vector3(connection.start.x,connection.start.y,0),new Vector3(connection.end.x,connection.end.y,0));
                EditorGUI.BeginChangeCheck();
                var start=(Vector2)Handles.PositionHandle(connection.start,Quaternion.identity);
                var end=(Vector2)Handles.PositionHandle(connection.end,Quaternion.identity);
                if(EditorGUI.EndChangeCheck()) {connection.start=start;connection.end=end;Build();Repaint();}
                if(connection.b?.piece!=null) foreach(var edge in Geometry.Edges(connection.b.piece.Draft().profile))
                    Handles.DrawLine(connection.b.transform.TransformPoint(edge.a)+(Vector3)connection.translationB,connection.b.transform.TransformPoint(edge.b)+(Vector3)connection.translationB);
            }
            var e=Event.current;
            if(tab==3&&Application.isPlaying&&level?.Session!=null&&e.type==EventType.MouseDown&&e.button==0&&e.shift)
            {
                var ray=HandleUtility.GUIPointToWorldRay(e.mousePosition);var plane=new Plane(Vector3.forward,Vector3.zero);
                if(plane.Raycast(ray,out var distance)){explosionOrigin=ray.GetPoint(distance);level.Explode(explosionOrigin,preset);Message(level.LastReport);e.Use();}
            }
        }
    }
}
