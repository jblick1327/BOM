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
    public sealed partial class BomAuthoringWindow
    {
        private const string PreferenceRoot="BOM.Authoring.";
        [SerializeField] private bool lockProportions=true,showSource=true;
        [SerializeField] private Vector2 previewOrbit;
        [SerializeField] private Material appearanceMaterial;
        private BomModelPreview modelPreview;
        private Label dimensionsLabel,draftState;
        private FloatField widthField,heightField;
        private string projectionKey;
        private ImportProposal[] projections;
        private bool orbiting;
        private Vector2 orbitPointer;
        private GameObject placementPreview;
        private Mesh placementMesh;
        private Material[] placementMaterials;
        private Texture2D placementTexture;

        private VisualElement Group(VisualElement parent,string title)
        {
            var group=new VisualElement();group.AddToClassList("bom-section");
            var heading=new Label(title);heading.AddToClassList("bom-section-title");group.Add(heading);parent.Add(group);return group;
        }
        private void BuildImportWorkspace()
        {
            if(source==null&&editing!=null)source=ImportService.Source(editing);
            if(source==null)showSource=false;
            if(appearanceMaterial==null)appearanceMaterial=ImportService.SourceMaterial(source);
            var model=Group(content,"Model");
            ObjectField<GameObject>(model,"Model (FBX)",source,SelectSource);
            var existing=new ObjectField("Piece") {objectType=typeof(PieceDefinition),value=editing,allowSceneObjects=false};
            existing.RegisterValueChangedCallback(e=>{if(!AllowDiscardDraft()){Build();return;}editing=e.newValue as PieceDefinition;
                draft=editing!=null?editing.Draft():null;source=ImportService.Source(editing);conversionDirty=false;draftEdited=false;
                appearanceProposed=editing!=null;appearanceEdits=AppearanceEdits.None;appearanceMaterial=ImportService.SourceMaterial(source);Build();});model.Add(existing);
            var modelActions=new VisualElement();modelActions.AddToClassList("bom-row");model.Add(modelActions);
            Button(modelActions,"New import",()=>{if(!AllowDiscardDraft())return;editing=null;source=null;draft=null;draftEdited=false;conversionDirty=false;appearanceProposed=false;appearanceEdits=AppearanceEdits.None;Build();});
            Button(modelActions,"Rebuild outline",()=>Generate(true),false,()=>CanProject).tooltip="Reprojects the model. Manual outline edits require confirmation.";
            if(draft==null){Help(model,"Drop an FBX to begin.");return;}

            var visuals=rootVisualElement.Q<VisualElement>("visuals");visuals.RemoveFromClassList("bom-hidden");
            var viewActions=new VisualElement();viewActions.AddToClassList("bom-row");visuals.Add(viewActions);
            foreach(bool sourceView in new[] {true,false})
            {
                bool selected=sourceView;var button=Button(viewActions,sourceView?"Source + outline":"Result",()=>{showSource=selected;previewOrbit=selected?Vector2.zero:new Vector2(18,0);previewDirty=true;rootVisualElement.Q<Button>("sourceView")?.EnableInClassList("bom-view-active",showSource);rootVisualElement.Q<Button>("resultView")?.EnableInClassList("bom-view-active",!showSource);});
                button.name=sourceView?"sourceView":"resultView";button.EnableInClassList("bom-view-active",showSource==sourceView);if(sourceView)button.SetEnabled(source!=null);
            }
            Button(viewActions,"Reset view",()=>{previewOrbit=showSource?Vector2.zero:new Vector2(18,0);profileEditor?.Fit();});
            image=new Image {name="modelPreview",scaleMode=ScaleMode.StretchToFill,focusable=true};image.AddToClassList("bom-model-preview");
            image.tooltip="Click a boundary point to select it. Right-drag to orbit. Edit points in the outline below.";visuals.Add(image);
            image.RegisterCallback<PointerDownEvent>(e=>
            {
                if(e.button==1){orbiting=true;orbitPointer=e.localPosition;image.CapturePointer(e.pointerId);e.StopPropagation();}
                else if(e.button==0&&modelPreview!=null&&modelPreview.TryProfilePoint(e.localPosition,image.contentRect.size,out var point))
                {profileEditor?.SelectProfilePoint(point,modelPreview.PickRadius(12,image.contentRect.height));e.StopPropagation();}
            });
            image.RegisterCallback<PointerMoveEvent>(e=>{if(!orbiting)return;var delta=(Vector2)e.localPosition-orbitPointer;
                previewOrbit=new Vector2(Mathf.Clamp(previewOrbit.x+delta.y*.4f,-75,75),previewOrbit.y+delta.x*.4f);orbitPointer=e.localPosition;});
            image.RegisterCallback<PointerUpEvent>(e=>{orbiting=false;if(image.HasPointerCapture(e.pointerId))image.ReleasePointer(e.pointerId);});
            image.RegisterCallback<PointerCaptureOutEvent>(_=>orbiting=false);
            var choices=new VisualElement {name="projectionChoices"};choices.AddToClassList("bom-row");visuals.Add(choices);BuildProjectionChoices(choices);
            profileEditor=new BomProfileEditor(draft,()=>{beforeDragEdited=draftEdited;BeginDraftEdit();},Changed);visuals.Add(profileEditor);
            profileEditor.DragCancelled=()=>{draftEdited=beforeDragEdited;hasUnsavedChanges=draftEdited;UpdateValidation();};
            profileEditor.InteractionFinished=()=>{SyncContourUI();RefreshActions();};
            var outlineTools=new VisualElement();outlineTools.AddToClassList("bom-row");visuals.Add(outlineTools);
            Button(outlineTools,"Fit",profileEditor.Fit);
            Button(outlineTools,"Delete point",profileEditor.RemoveSelected,false,()=>profileEditor.CanRemove);
            Button(outlineTools,"Add hole",AddHole,false,()=>Geometry.TryValidate(draft.profile,out _));
            Button(outlineTools,"Delete hole",()=>{BeginDraftEdit();draft.profile.holes.RemoveAt(profileEditor.SelectedRing-1);draft.import.manuallyCorrected=true;Changed();Build();},false,()=>profileEditor.SelectedRing>0);
            var rings=new DropdownField("Contour",new[] {"Outer"}.Concat(Enumerable.Range(1,draft.profile.holes.Count).Select(i=>"Hole "+i)).ToList(),0);
            rings.RegisterValueChangedCallback(_=>profileEditor.SelectRing(rings.index));outlineTools.Add(rings);
            var pointField=new Vector2Field("Point (m)") {name="selectedPoint"};pointField.SetEnabled(false);visuals.Add(pointField);
            pointField.RegisterValueChangedCallback(e=>{profileEditor.SetSelectedPoint(e.newValue);pointField.SetValueWithoutNotify(profileEditor.SelectedPoint);});
            profileEditor.SelectionChanged=()=>{rings.SetValueWithoutNotify(rings.choices[profileEditor.SelectedRing]);pointField.SetValueWithoutNotify(profileEditor.SelectedPoint);pointField.SetEnabled(profileEditor.HasSelection);RefreshActions();};
            if(draft.disconnectedProposal.Count>0)
            {
                Button(outlineTools,"Keep selected region",KeepSelectedRegion);
                Help(visuals,"Click a region to keep.");
            }
            dimensionsLabel=new Label {name="dimensions"};dimensionsLabel.AddToClassList("bom-dimensions");visuals.Add(dimensionsLabel);
            validation=new Label {name="validation"};validation.AddToClassList("bom-report");visuals.Add(validation);

            var size=Group(content,"Size");
            var dimensions=DraftDimensions();widthField=new FloatField("Width (m)") {name="targetWidth",value=dimensions.x,isDelayed=true};
            heightField=new FloatField("Height (m)") {name="targetHeight",value=dimensions.y,isDelayed=true};size.Add(widthField);size.Add(heightField);
            widthField.RegisterValueChangedCallback(e=>{if(e.currentTarget==widthField&&windowEnabled&&widthField.enabledInHierarchy)ResizeDraft(e.newValue,true);});
            heightField.RegisterValueChangedCallback(e=>{if(e.currentTarget==heightField&&windowEnabled&&heightField.enabledInHierarchy)ResizeDraft(e.newValue,false);});
            var proportions=new Toggle("Lock proportions") {value=lockProportions};proportions.RegisterValueChangedCallback(e=>lockProportions=e.newValue);size.Add(proportions);
            Float(size,"Thickness (m)",draft.thickness,v=>{draft.thickness=v;appearanceEdits|=AppearanceEdits.Thickness;});
            size.tooltip="Dimensions are in metres. X/Y resizing changes area and mass; thickness controls the visual extrusion.";

            var physics=Group(content,"Physical properties");
            ObjectField<MaterialDefinition>(physics,"Physics material",draft.material,v=>{BeginDraftEdit();draft.material=v;Changed();Build();});
            DefinitionActions(physics,"material",draft.material,v=>{BeginDraftEdit();draft.material=v;Changed();Build();},()=>DuplicateMaterial(),"New material",()=>CreateMaterial());
            InlineProperties(physics,draft.material,"density","blastResistance","friction","restitution","linearDamping","angularDamping");
            var mode=new DropdownField("Destruction",new System.Collections.Generic.List<string> {"Destructible","Indestructible"},draft.destruction.mode==DestructionMode.Subtract?0:1);
            mode.RegisterValueChangedCallback(_=>{BeginDraftEdit();draft.destruction.mode=mode.index==0?DestructionMode.Subtract:DestructionMode.Indestructible;Changed();Build();});physics.Add(mode);
            if(draft.destruction.mode==DestructionMode.Subtract)
            {
                ObjectField<DestructionResponse>(physics,"Response",draft.destruction.response,v=>{BeginDraftEdit();draft.destruction.response=v;Changed();Build();});
                DefinitionActions(physics,"response",draft.destruction.response,v=>{BeginDraftEdit();draft.destruction.response=v;Changed();Build();},()=>DuplicateResponse(),"New response",()=>CreateResponse());
                InlineProperties(physics,draft.destruction.response,"impulseScale");
            }
            Float(physics,"Push multiplier",draft.destruction.impulseMultiplier,v=>draft.destruction.impulseMultiplier=v);
            physics.tooltip="Density controls mass. Blast push adds speed, multiplied by the explosion preset, response and piece settings.";
            Toggle(physics,"Grabbable",draft.grabEligible,v=>draft.grabEligible=v);

            var appearance=Group(content,"Appearance");
            ObjectField<Material>(appearance,"Unity material",appearanceMaterial,v=>appearanceMaterial=v);
            Button(appearance,"Import appearance",()=>{BeginDraftEdit();ImportService.ReadAppearance(appearanceMaterial,draft.appearance);draft.appearance.approvedImagePng=null;
                appearanceEdits|=AppearanceEdits.Face|AppearanceEdits.Image;Changed();Build();},false,()=>appearanceMaterial!=null).tooltip="Copies the base colour and texture. The result uses BOM's extrusion shader.";
            Colour(appearance,"Face",draft.appearance.face,v=>{draft.appearance.face=v;appearanceEdits|=AppearanceEdits.Face;});
            Colour(appearance,"Sides",draft.appearance.side,v=>{draft.appearance.side=v;appearanceEdits|=AppearanceEdits.Side;});
            Colour(appearance,"Cuts",draft.appearance.cut,v=>{draft.appearance.cut=v;appearanceEdits|=AppearanceEdits.Cut;});
            ObjectField<Texture2D>(appearance,"Face texture",draft.appearance.image,v=>{BeginDraftEdit();draft.appearance.image=v;draft.appearance.approvedImagePng=null;appearanceEdits|=AppearanceEdits.Image;Changed();});
            Button(appearance,"Fit texture",FitImage,false,()=>Geometry.TryValidate(draft.profile,out _));
            var advanced=new Foldout {text="Advanced: geometry and mapping",value=false};content.Add(advanced);
            var plane=new EnumField("Projection plane",draft.import.plane);plane.RegisterValueChangedCallback(e=>SetProjection((ProjectionPlane)e.newValue));advanced.Add(plane);
            var orientation=new Vector3Field("Source orientation (degrees)") {value=draft.import.orientation};orientation.RegisterValueChangedCallback(e=>{BeginDraftEdit();draft.import.orientation=e.newValue;ConversionChanged();});advanced.Add(orientation);
            var scale=new FloatField("Source scale (metres per imported unit)") {value=draft.import.scale,isDelayed=true};scale.RegisterValueChangedCallback(e=>{BeginDraftEdit();draft.import.scale=e.newValue;ConversionChanged();});advanced.Add(scale);
            var origin=new Vector2Field("Projected origin") {value=draft.import.origin};origin.RegisterValueChangedCallback(e=>{BeginDraftEdit();draft.import.origin=e.newValue;ConversionChanged();});advanced.Add(origin);
            var snap=new Toggle("Snap to grid");snap.RegisterValueChangedCallback(e=>profileEditor.Snap=e.newValue);advanced.Add(snap);
            var grid=new FloatField("Grid spacing (m)") {value=profileEditor.GridSize,isDelayed=true};grid.RegisterValueChangedCallback(e=>{if(Geometry.Positive(e.newValue)&&e.newValue>=Geometry.Tolerance)profileEditor.GridSize=e.newValue;else grid.SetValueWithoutNotify(profileEditor.GridSize);});advanced.Add(grid);
            contourCoordinates=new VisualElement {name="contourCoordinates"};advanced.Add(contourCoordinates);SyncContourUI();
            Vector(advanced,"Face U vector",draft.appearance.u,v=>{draft.appearance.u=v;appearanceEdits|=AppearanceEdits.U;});Vector(advanced,"Face V vector",draft.appearance.v,v=>{draft.appearance.v=v;appearanceEdits|=AppearanceEdits.V;});Vector(advanced,"UV offset",draft.appearance.offset,v=>{draft.appearance.offset=v;appearanceEdits|=AppearanceEdits.Offset;});
            var wrap=new EnumField("Image wrap",draft.appearance.wrapMode);wrap.RegisterValueChangedCallback(e=>{BeginDraftEdit();draft.appearance.wrapMode=(TextureWrapMode)e.newValue;appearanceEdits|=AppearanceEdits.Wrap;Changed();});advanced.Add(wrap);
            var filter=new EnumField("Image filtering",draft.appearance.filterMode);filter.RegisterValueChangedCallback(e=>{BeginDraftEdit();draft.appearance.filterMode=(FilterMode)e.newValue;appearanceEdits|=AppearanceEdits.Filter;Changed();});advanced.Add(filter);
            if(!string.IsNullOrEmpty(inference))advanced.tooltip=inference;
            Button(advanced,"Restore saved outline",()=>Generate(false),false,()=>CanProject&&editing!=null);
            var footer=rootVisualElement.Q<VisualElement>("footer");footer.RemoveFromClassList("bom-hidden");
            draftState=new Label {name="draftState"};footer.Add(draftState);
            approveButton=Button(footer,"Save piece",()=>Approve(),true,()=>CanApprove);
            continueButton=Button(footer,"Save and place",SaveAndPlace,true,()=>CanApprove);
            Button(footer,"Discard edits",()=>{BeginDraftEdit();draft=editing!=null?editing.Draft():null;conversionDirty=false;draftEdited=false;appearanceProposed=editing!=null;appearanceEdits=AppearanceEdits.None;inference=null;Build();Message("Edits discarded.");});
            UpdateValidation();previewDirty=true;
        }
        private void DefinitionActions<T>(VisualElement parent,string id,T asset,Action<T> select,Action duplicate,string newLabel,Action create,bool available=true) where T:ScriptableObject
        {
            var row=new VisualElement();row.AddToClassList("bom-row");parent.Add(row);
            var menu=new ToolbarMenu {text="Presets",name=id+"Presets"};row.Add(menu);
            var starters=StarterPresets.All<T>();menu.SetEnabled(starters.Length>0);
            foreach(var starter in starters)
            {var chosen=starter;menu.menu.AppendAction(chosen.name,_=>select(chosen),chosen==asset?DropdownMenuAction.Status.Checked:DropdownMenuAction.Status.Normal);}
            menu.tooltip="Choose a starter preset. Duplicate it to customize its values.";
            Button(row,"Duplicate",duplicate,false,()=>available&&asset!=null&&!Application.isPlaying).name=id+"Duplicate";
            Button(row,newLabel,create,false,()=>available&&!Application.isPlaying);
            if(asset!=null)
            {
                var sharing=new Label(StarterPresets.IsStarter(asset)?"Starter":"Shared") {name=id+"Sharing",tooltip=StarterPresets.Description(asset)};
                sharing.AddToClassList("bom-asset-note");row.Add(sharing);
            }
        }
        private void InlineProperties(VisualElement parent,ScriptableObject asset,params string[] names)
        {
            if(asset==null)return;
            var serialized=new SerializedObject(asset);var fields=new VisualElement {tooltip=StarterPresets.IsStarter(asset)?"Duplicate this starter to edit an independent copy.":"Shared asset: edits are saved immediately and affect every use. Discarding piece edits does not revert these values."};parent.Add(fields);
            fields.SetEnabled(!StarterPresets.IsStarter(asset));
            Foldout extra=null;
            for(int i=0;i<names.Length;i++)
            {
                string name=names[i];string label=name=="density"?"Density (kg/m\u00b2)":name=="radius"?"Max radius (m)":
                    name=="impulseScale"?"Blast push":name=="impulseSpeed"?"Added speed (m/s)":null;
                var property=new PropertyField(serialized.FindProperty(name),label) {name="physics-"+name};
                if(i<2||asset is ExplosionPreset)fields.Add(property);
                else {if(extra==null){extra=new Foldout {text="More physics",value=false};fields.Add(extra);}extra.Add(property);}
            }
            fields.Bind(serialized);fields.RegisterCallback<SerializedPropertyChangeEvent>(_=>{EditorUtility.SetDirty(asset);AssetDatabase.SaveAssetIfDirty(asset);previewDirty=true;UpdateValidation();RefreshActions();});
        }
        private void BuildProjectionChoices(VisualElement parent)
        {
            if(source==null)return;
            var settings=draft.import.Clone();settings.plane=ProjectionPlane.XY;
            string key=source.GetEntityId().ToString()+JsonUtility.ToJson(settings)+AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(source));
            if(projections==null||projectionKey!=key)
            {
                projectionKey=key;projections=new ImportProposal[3];
                for(int i=0;i<3;i++){settings.plane=(ProjectionPlane)i;projections[i]=ImportService.Propose(source,settings:settings);}
            }
            string[] names={"Front (XY)","Top (XZ)","Side (YZ)"};
            for(int i=0;i<3;i++)
            {
                int index=i;var button=Button(parent,names[i],()=>SetProjection((ProjectionPlane)index));button.name="projection"+i;
                button.AddToClassList("bom-projection");button.EnableInClassList("bom-view-active",draft.import.plane==(ProjectionPlane)i);
                button.Add(new BomSilhouette(projections[i].draft.profile));
                bool valid=Geometry.TryValidate(projections[i].draft.profile,out _);button.tooltip=valid?names[i]:"No solid silhouette in this view.";
                if(!Geometry.TryValidate(draft.profile,out _)&&valid&&draft.profile.outer.points.Count<3)button.AddToClassList("bom-suggested");
            }
        }
        private void SetProjection(ProjectionPlane plane)
        {if(draft.import.plane==plane)return;BeginDraftEdit();draft.import.plane=plane;ConversionChanged();previewOrbit=Vector2.zero;}
        private Vector2 DraftDimensions()
        {
            var points=draft?.profile?.outer?.points?.Where(Geometry.Finite).ToArray();
            return points==null||points.Length==0?Vector2.zero:new Vector2(points.Max(p=>p.x)-points.Min(p=>p.x),points.Max(p=>p.y)-points.Min(p=>p.y));
        }
        private void ResizeDraft(float target,bool width)
        {
            var old=DraftDimensions();float current=width?old.x:old.y;
            if(!Geometry.Positive(target)||!Geometry.Positive(current)){UpdateDimensions();return;}
            float ratio=target/current;var factor=lockProportions?new Vector2(ratio,ratio):width?new Vector2(ratio,1):new Vector2(1,ratio);
            if(factor==Vector2.one)return;
            BeginDraftEdit();draft.profile=Geometry.Transform(draft.profile,p=>Vector2.Scale(p,factor));
            draft.disconnectedProposal=draft.disconnectedProposal.Select(r=>Geometry.Transform(r,p=>Vector2.Scale(p,factor))).ToList();
            draft.import.projectedScale=Vector2.Scale(draft.import.projectedScale,factor);
            draft.appearance.u=new Vector2(draft.appearance.u.x/factor.x,draft.appearance.u.y/factor.y);
            draft.appearance.v=new Vector2(draft.appearance.v.x/factor.x,draft.appearance.v.y/factor.y);
            appearanceEdits|=AppearanceEdits.U|AppearanceEdits.V;Changed();Build();
        }
        private void UpdateDimensions()
        {
            var d=DraftDimensions();widthField?.SetValueWithoutNotify(d.x);heightField?.SetValueWithoutNotify(d.y);
            widthField?.SetEnabled(d.x>0);heightField?.SetEnabled(d.y>0);
            if(dimensionsLabel!=null)
            {
                dimensionsLabel.text=$"{d.x:0.#####} \u00d7 {d.y:0.#####} m \u00b7 thickness {draft.thickness:0.#####} m";
                if(draft.material!=null&&draft.material.TryValidate(out _)&&Geometry.TryValidate(draft.profile,out _))
                    dimensionsLabel.text+=$" \u00b7 {Geometry.Area(draft.profile)*draft.material.density:0.###} kg";
            }
            if(draftState!=null)draftState.text=conversionDirty?"Rebuild needed":draftEdited?"Unsaved edits":editing!=null?"Saved":"New piece";
        }
        private void KeepSelectedRegion()
        {
            if(draft.disconnectedProposal.Count==0)return;
            int index=profileEditor.SelectedRegion;BeginDraftEdit();
            if(index>0&&index<=draft.disconnectedProposal.Count)draft.profile=draft.disconnectedProposal[index-1].Clone();
            draft.disconnectedProposal.Clear();draft.import.manuallyCorrected=true;Changed();Build();
        }
        private void RememberSelections()
        {
            foreach(var pair in new[] {("Material",(UnityEngine.Object)draft.material),("Response",(UnityEngine.Object)draft.destruction.response)})
            {string path=AssetDatabase.GetAssetPath(pair.Item2);if(!string.IsNullOrEmpty(path))EditorPrefs.SetString(PreferenceRoot+pair.Item1,AssetDatabase.AssetPathToGUID(path));}
            string saved=AssetDatabase.GetAssetPath(editing);if(!string.IsNullOrEmpty(saved))EditorPrefs.SetString(PreferenceRoot+"Folder",Path.GetDirectoryName(saved).Replace('\\','/'));
        }
        private void RestoreSelections(PieceDraft proposed)
        {
            T Last<T>(string key) where T:UnityEngine.Object => AssetDatabase.LoadAssetAtPath<T>(AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(PreferenceRoot+key,"")));
            if(proposed.material==null)proposed.material=Last<MaterialDefinition>("Material")??StarterPresets.Get<MaterialDefinition>("Default");
            if(proposed.destruction.response==null)proposed.destruction.response=Last<DestructionResponse>("Response")??StarterPresets.Get<DestructionResponse>("Standard");
        }
        private void SaveAndPlace()
        {
            if(!Approve())return;if(level==null)CreateLevel();placementAsset=editing;tab=1;Build();StartPlacementPreview();
        }
        private void StartPlacementPreview()
        {
            ClearPlacementPreview();if(level==null||placementAsset==null||!placementAsset.IsApproved||Application.isPlaying)return;
            var d=placementAsset.Draft();placementPreview=new GameObject("BOM placement preview") {hideFlags=HideFlags.HideAndDontSave};
            placementMesh=Extrusion.Build(Extrusion.PreviewState(d));placementMaterials=Extrusion.Materials(new AppearanceRecord {face=d.appearance.face,
                side=d.appearance.side,cut=d.appearance.cut,imagePng=d.appearance.approvedImagePng,wrapMode=d.appearance.wrapMode,filterMode=d.appearance.filterMode},out placementTexture);
            placementPreview.AddComponent<MeshFilter>().sharedMesh=placementMesh;placementPreview.AddComponent<MeshRenderer>().sharedMaterials=placementMaterials;
            placementPreview.transform.rotation=level.transform.rotation;placementPreview.transform.localScale=level.transform.lossyScale;
            SceneView.lastActiveSceneView?.Focus();Message("Click to place \u00b7 Esc to cancel");SceneView.RepaintAll();
        }
        private void ClearPlacementPreview()
        {
            if(placementPreview!=null)DestroyImmediate(placementPreview);if(placementMesh!=null)DestroyImmediate(placementMesh);
            if(placementMaterials!=null)foreach(var material in placementMaterials)if(material!=null)DestroyImmediate(material);
            if(placementTexture!=null)DestroyImmediate(placementTexture);placementPreview=null;placementMesh=null;placementMaterials=null;placementTexture=null;
        }
        private void PlacePieceAt(Vector2 position)
        {
            if(level==null||placementAsset==null||!placementAsset.IsApproved||Application.isPlaying)return;
            var go=new GameObject(placementAsset.name);Undo.RegisterCreatedObjectUndo(go,"Place BOM piece");go.transform.SetParent(level.transform,false);
            go.transform.position=position;var placement=go.AddComponent<PiecePlacement>();placement.piece=placementAsset;
            Selection.activeGameObject=go;EditorSceneManager.MarkSceneDirty(level.gameObject.scene);
            var d=placementAsset.Draft();var points=d.profile.outer.points.Select(p=>go.transform.TransformPoint(p)).ToArray();
            var bounds=new Bounds(points[0],Vector3.zero);foreach(var p in points)bounds.Encapsulate(p);
            SceneView.lastActiveSceneView?.Frame(bounds,false);ClearPlacementPreview();Build();Message($"Placed {placementAsset.name}");
        }
        private bool PlacementSceneGUI(SceneView view)
        {
            if(placementPreview==null)return false;
            if(Application.isPlaying||level==null){ClearPlacementPreview();return false;}
            var e=Event.current;
            if(e.type==EventType.KeyDown&&e.keyCode==KeyCode.Escape){ClearPlacementPreview();Message("Placement cancelled.");e.Use();return true;}
            if(e.alt)return false;
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            var ray=HandleUtility.GUIPointToWorldRay(e.mousePosition);var plane=new Plane(Vector3.forward,Vector3.zero);
            if(plane.Raycast(ray,out float distance))
            {
                var position=ray.GetPoint(distance);position.z=0;placementPreview.transform.position=position;
                var d=placementAsset.Draft();Handles.color=Color.cyan;
                foreach(var edge in Geometry.Edges(d.profile))Handles.DrawLine(placementPreview.transform.TransformPoint(edge.a),placementPreview.transform.TransformPoint(edge.b));
                Handles.Label(position,$"{DraftPlacementSize(d):0.###} m wide");
                if(e.type==EventType.MouseDown&&e.button==0){PlacePieceAt(position);e.Use();return true;}
            }
            if(e.type==EventType.MouseMove)view.Repaint();return true;
        }
        private float DraftPlacementSize(PieceDraft d)=> (d.profile.outer.points.Max(p=>p.x)-d.profile.outer.points.Min(p=>p.x))*level.transform.lossyScale.x;
        private void RenderImportPreview()
        {
            if(!windowEnabled||previewUpdating)return;previewUpdating=true;
            try
            {
                if(EditorApplication.timeSinceStartup>=nextControls){nextControls=EditorApplication.timeSinceStartup+.4;UpdateValidation();RefreshActions();}
                if(tab!=0||draft==null||image==null||Application.isPlaying||EditorApplication.timeSinceStartup<nextPreview)return;
                nextPreview=EditorApplication.timeSinceStartup+.08;
                if(modelPreview==null){modelPreview=new BomModelPreview();preview=modelPreview.Utility;}
                image.image=modelPreview.Render(source,draft,profileEditor,showSource,previewOrbit,image.contentRect.size,previewDirty);previewDirty=false;
            }
            catch(Exception e){Message("Preview: "+e.Message);}
            finally{previewUpdating=false;}
        }
    }
}
