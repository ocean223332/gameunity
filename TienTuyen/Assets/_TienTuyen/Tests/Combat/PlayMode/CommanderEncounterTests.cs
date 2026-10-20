using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
namespace TienTuyen.Combat.Tests
{
 public sealed class CommanderEncounterTests
 {
  CombatGame game;
  object Read(string name){var p=typeof(CombatGame).GetProperty(name);Assert.That(p,Is.Not.Null,name);return p.GetValue(game);}
  [UnitySetUp]public IEnumerator Setup(){yield return SceneManager.LoadSceneAsync("CombatSpike");game=Object.FindFirstObjectByType<CombatGame>();game.enabled=false;yield return null;game.BeginRun();game.SetViewInput(0,Vector3.forward*10,false);}
  [UnityTest]public IEnumerator BossCheatStartsWaveFiveAndCinematicFreezesRunTimer(){game.ExecuteCheat("boss");Assert.That(game.Wave,Is.EqualTo(5));Assert.That((bool)Read("CommanderCinematic"),Is.True);float before=game.RunElapsed;game.StepSimulation(.5f,Vector2.one);Assert.That(game.RunElapsed,Is.EqualTo(before));Assert.That(game.DebugCheatsUsed,Is.True);yield break;}
  [UnityTest]public IEnumerator BossCannotBeSkippedForRewardAndRestartClearsEncounter(){game.ExecuteCheat("boss");game.ExecuteCheat("nextwave");Assert.That(game.Wave,Is.EqualTo(5));game.Restart();Assert.That((bool)Read("CommanderActive"),Is.False);Assert.That(game.Run.LauncherSlot,Is.EqualTo(-1));yield break;}
  [UnityTest]public IEnumerator DefeatAwardsLauncherOnceAfterRetreatAndEnablesUpgrade(){game.ExecuteCheat("boss");game.StepSimulation(10,Vector2.zero);Assert.That((bool)Read("CommanderCinematic"),Is.False);var hit=typeof(CombatGame).GetMethod("DamageCommander");Assert.That(hit,Is.Not.Null);hit.Invoke(game,new object[]{100000f});Assert.That((bool)Read("CommanderCinematic"),Is.True);game.StepSimulation(10,Vector2.zero);Assert.That(game.Run.CommanderRewardClaimed,Is.True);Assert.That(game.Run.LauncherSlot,Is.GreaterThanOrEqualTo(0));Assert.That(game.State,Is.EqualTo(CombatState.Upgrade));long money=game.Run.CurrencyMinor;hit.Invoke(game,new object[]{100000f});game.StepSimulation(10,Vector2.zero);Assert.That(game.Run.CurrencyMinor,Is.EqualTo(money));yield break;}
 }
}
