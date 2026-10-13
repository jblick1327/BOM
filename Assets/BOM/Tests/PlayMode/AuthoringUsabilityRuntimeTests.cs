using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BOM.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace BOM.Tests
{
    [Category("AuthoringUsability")]
    public sealed class AuthoringUsabilityRuntimeTests
    {
        private readonly List<UnityEngine.Object> owned=new List<UnityEngine.Object>();
        private T Own<T>(T value) where T:UnityEngine.Object {owned.Add(value);return value;}
        [UnityTearDown] public IEnumerator Cleanup()
        {foreach(var item in owned.AsEnumerable().Reverse())if(item!=null){if(item is EditorWindow w){w.DiscardChanges();w.Close();}else Object.DestroyImmediate(item);}owned.Clear();yield return null;}
        [UnityTest] public IEnumerator NamedPresetUsesFrozenCapturedOrderAndRunsTheRealExplosion()
        {
            var material=Own(ScriptableObject.CreateInstance<MaterialDefinition>());var response=Own(ScriptableObject.CreateInstance<DestructionResponse>());
            var piece=Own(ScriptableObject.CreateInstance<PieceDefinition>());
            Assert.That(piece.TryApprove(new PieceDraft {profile=Geometry.Rectangle(-.5f,-.5f,.5f,.5f),material=material,destruction=new DestructionSelection {response=response}},out var report),Is.True,report.ToString());
            var root=Own(new GameObject("Named preset runtime test"));var level=root.AddComponent<LevelAuthoring>();level.startOnPlay=false;level.gravity=Vector2.zero;
            var placed=new GameObject("Target");placed.transform.SetParent(root.transform);placed.transform.position=new Vector3(3,0,0);placed.AddComponent<PiecePlacement>().piece=piece;
            var near=Own(ScriptableObject.CreateInstance<ExplosionPreset>());near.name="Near test";near.radius=.25f;near.reachBudget=.25f;near.impulseSpeed=0;
            var far=Own(ScriptableObject.CreateInstance<ExplosionPreset>());far.name="Far test";far.radius=6;far.reachBudget=10;far.impulseSpeed=0;
            level.testPresets.Add(near);level.testPresets.Add(far);Assert.That(level.StartTest(),Is.True,level.LastReport);
            level.testPresets.Reverse();far.name="Edited after start";far.radius=.1f;far.reachBudget=.1f;
            var window=Own(ScriptableObject.CreateInstance<BomAuthoringWindow>());
            typeof(BomAuthoringWindow).GetField("tab",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(window,3);
            typeof(BomAuthoringWindow).GetField("level",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(window,level);
            window.Show();window.rootVisualElement.Clear();window.CreateGUI();yield return null;
            var choices=window.rootVisualElement.Q<DropdownField>("explosionPreset");Assert.That(choices.choices[0],Does.Contain("Near test"));Assert.That(choices.choices[1],Does.Contain("Far test"));
            choices.index=1;Assert.That(level.Session.BodyCount,Is.EqualTo(1));
            var run=window.rootVisualElement.Query<Button>().ToList().Single(b=>b.text=="Run explosion");Assert.That(run.enabledInHierarchy,Is.True);
            using(var e=NavigationSubmitEvent.GetPooled()){e.target=run;run.SendEvent(e);}
            Assert.That(level.LastReport,Is.EqualTo("Explosion committed."));Assert.That(level.Session.BodyCount,Is.Zero);
        }
    }
}
