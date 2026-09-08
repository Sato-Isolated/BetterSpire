from pathlib import Path
r=Path('.')
(r/'tests/NativePreviewTests.cs').write_text((r/'.github/migrations/NativePreviewTests.cs.txt').read_text())
(r/'tests/NativePreview.Core.Tests.csproj').write_text('''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net9.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>enable</Nullable></PropertyGroup>
  <ItemGroup>
    <Compile Include="NativePreviewTests.cs" />
    <Compile Include="../BetterSpire2/HandViewer/DetachedCardPreview.cs" Link="DetachedCardPreview.cs" />
    <Compile Include="../BetterSpire2/Guardian/Core/ForecastHookPolicy.cs" Link="ForecastHookPolicy.cs" />
  </ItemGroup>
</Project>
''')
p=r/'tests/GameApiCompatibilityTests.cs';s=p.read_text()
s=s.replace('            VerifyWinPatchTarget(game, mod);','            VerifyWinPatchTarget(game, mod);\n            VerifyAdditionalContracts(game, mod, root);')
a=s.index('    private static void VerifyManifest(')
s=s[:a]+'''    private static void VerifyAdditionalContracts(MetadataAssembly game, MetadataAssembly mod, string root)
    {
        const string card = "MegaCrit.Sts2.Core.Models.CardModel";
        const string model = "MegaCrit.Sts2.Core.Models.AbstractModel";
        const string state = "MegaCrit.Sts2.Core.Combat.CombatState";
        const string manager = "MegaCrit.Sts2.Core.Combat.CombatManager";
        const string runManager = "MegaCrit.Sts2.Core.Runs.RunManager";
        const string variables = "MegaCrit.Sts2.Core.Localization.DynamicVars.DynamicVarSet";
        game.RequireContract(model, "MutableClone", model, MethodAttributes.Public, false);
        game.RequireContract(card, "UpdateDynamicVarPreview", "System.Void", MethodAttributes.Public, false,
            "MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode", "MegaCrit.Sts2.Core.Entities.Creatures.Creature", variables);
        game.RequireContract(card, "GetDescriptionForPile", "System.String", MethodAttributes.Public, false,
            "MegaCrit.Sts2.Core.Entities.Cards.PileType", "MegaCrit.Sts2.Core.Entities.Creatures.Creature");
        game.RequireContract("MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry", "get_RoundNumber",
            "System.Int32", MethodAttributes.Private, false);
        game.RequireContract("MegaCrit.Sts2.Core.Combat.History.CombatHistoryEntry", "get_CurrentSide",
            "MegaCrit.Sts2.Core.Combat.CombatSide", MethodAttributes.Private, false);
        game.RequireContract("MegaCrit.Sts2.Core.Models.Powers.PoisonPower", "get_TriggerCount",
            "System.Int32", MethodAttributes.Private, false);
        game.RequireContract("MegaCrit.Sts2.Core.Models.Relics.BeatingRemnant", "get_DamageReceivedThisTurn",
            "System.Decimal", MethodAttributes.Private, false);
        foreach (string prefix in new[] { "add_", "remove_" })
        {
            game.RequireContract(runManager, prefix + "RunStarted", "System.Void", MethodAttributes.Public, false,
                "System.Action<MegaCrit.Sts2.Core.Runs.RunState>");
            game.RequireContract("MegaCrit.Sts2.Core.Combat.CombatStateTracker", prefix + "CombatStateChanged",
                "System.Void", MethodAttributes.Public, false, "System.Action<" + state + ">");
        }
        foreach (string name in new[] { "Tick", "CanTrack", "OnCombatSetUp" })
            Check(!mod.Calls("BetterSpire2.Journal.Game.JournalService", name, manager, "DebugOnlyGetState", 0),
                "journal " + name + " never polls debug combat state");
        Check(mod.Calls("BetterSpire2.Runtime.RunLifecycle", "EnsureAttached", runManager, "add_RunStarted", 1),
            "native run observer attaches");
        Check(mod.Calls("BetterSpire2.Runtime.RunLifecycle", "Stop", runManager, "remove_RunStarted", 1),
            "native run observer detaches");
        const string preview = "BetterSpire2.HandViewer.DetachedCardPreview";
        Check(mod.Calls(preview, "Description", model, "MutableClone", 0), "native deep clone for isolated preview");
        Check(!mod.Calls(preview, "Description", card, "CreateClone", 0), "preview never registers with CardScope");
        Check(mod.Calls(preview, "Description", card, "UpdateDynamicVarPreview", 3), "native preview writes detached output");
        const string hand = "BetterSpire2.HandViewer.TeammateHandViewer+PlayerHandSection";
        Check(!mod.Calls(hand, "RefreshCards", card, "UpdateDynamicVarPreview", 3), "hand no longer writes live preview");
        Check(!mod.Calls(hand, "UpdateCard", "Godot.Node", "QueueFree", 0), "card changes preserve node subtree");
        Check(mod.ParameterCounts("BetterSpire2.Guardian.Core.ForecastHookPolicy", "IsReaction").Contains(1),
            "conservative lifecycle policy included");
        // Test the inspected native hazard as well as source-linked isolation behavior.
        Check(game.Calls(card, "DeepCloneFields", card, "EnchantInternal", 2), "native clone reapplies enchantment");
        Check(game.Calls(card, "EnchantInternal", "System.Action", "Invoke", 0), "native clone can invoke shallow event subscribers");
        string snapshot = Path.Combine(root, "references", "v111", "sts2.dll");
        if (File.Exists(snapshot))
            Check(File.ReadAllBytes(snapshot).SequenceEqual(File.ReadAllBytes(Path.Combine(root, "references", "sts2.dll"))),
                "canonical references equal audited v111 snapshot");
    }

'''+s[a:]
a=s.index('        private TypeDefinitionHandle? FindType(')
s=s[:a]+'''        internal void RequireContract(string owner, string name, string returnType,
            MethodAttributes access, bool isStatic, params string[] parameters)
        {
            var type = _metadata.GetTypeDefinition(FindType(owner) ?? throw new TypeLoadException(owner));
            bool found = type.GetMethods().Select(h => _metadata.GetMethodDefinition(h)).Any(method =>
            {
                if (_metadata.GetString(method.Name) != name) return false;
                var signature = method.DecodeSignature(_names, null);
                return signature.ReturnType == returnType && signature.ParameterTypes.SequenceEqual(parameters) &&
                    (method.Attributes & MethodAttributes.MemberAccessMask) == access &&
                    ((method.Attributes & MethodAttributes.Static) != 0) == isStatic;
            });
            Check(found, "visibility/static/return/parameters: " + owner + "." + name);
        }

'''+s[a:];p.write_text(s)
for p in [r/'BetterSpire2Lite.csproj',r/'Properties/AssemblyInfo.cs',r/'README.md',r/'tests/GameApiCompatibilityTests.cs',r/'BetterSpire2/Core/ModEntry.cs',r/'BetterSpire2/Guardian/DamageTracker.cs']:
 s=p.read_text().replace('3.6.0-v111','3.6.1-v111').replace('3.6.0.0','3.6.1.0').replace('BetterSpire 3.6.0 for','BetterSpire 3.6.1 for');p.write_text(s)
for file in ['build.sh','build.ps1']:
 p=r/file;s=p.read_text().replace('/9 ', '/11 ').replace('8/11 ', '10/11 ').replace('9/11 ', '11/11 ')
 if file=='build.sh':
  marker="echo '10/11 Building mod...'"
  addition="""echo '8/11 Checking native journal accounting and damage chronology...'
dotnet run --project tests/NativeJournal.Core.Tests.csproj --configuration Release
echo '9/11 Checking isolated previews and forecast coverage...'
dotnet run --project tests/NativePreview.Core.Tests.csproj --configuration Release
"""
 else:
  marker="    Write-Host '10/11 Building the actual mod against the supplied DLL references...'"
  addition="""    Write-Host '8/11 Checking native journal accounting and damage chronology...'
    & dotnet run --project (Join-Path $root 'tests/NativeJournal.Core.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Native journal tests failed. No release was produced.' }
    Write-Host '9/11 Checking isolated previews and forecast coverage...'
    & dotnet run --project (Join-Path $root 'tests/NativePreview.Core.Tests.csproj') --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Preview isolation tests failed. No release was produced.' }
"""
 assert marker in s;s=s.replace(marker,addition+marker)
 s=s.replace('Built locally:', 'Build timestamp:').replace('v0.111-API-IL-contract tests','native-journal/preview-isolation/v0.111-API-IL-contract tests').replace('v0.111 API/IL contract tests:', 'native-journal, preview-isolation and v0.111 API/IL contract tests:')
 p.write_text(s)
p=r/'BetterSpire2/DamageMeter/UI/DamageMeterHud.cs';s=p.read_text().replace('(_snapshot?.IsPartial == true ? "\\n" + ModText.T("Partial data: some damage could not be observed.") : "");','''(_snapshot?.IsPartial == true ? "\\n" + ModText.T("Partial data: some damage could not be observed.") : "") +
            "\\n" + T("Percentages use attributed damage only. Poison is shared by observed stack contribution.",
                "Pourcentages sur les dégâts attribués uniquement. Poison réparti selon les contributions observées.");''');p.write_text(s)
p=r/'README.md';s=p.read_text();a=s.index('The obsolete Python audit runner');b=s.index('## Repository layout',a)
s=s[:a]+'''## Native migration 3.6.1-v111

The v0.111 reference assembly was inspected with ILSpy without executing the game.
Journal collection uses `RunManager.RunStarted` and cached `CombatSetUp` state. Debug
reads remain only in one-time late initialization. Combat identity fences reject stale
callbacks. Early victory and history-clear patches remain: history is cleared before
`CombatWon` / `CombatEnded`. Private native getters stay behind cached reflection because
the inspected DLL does not expose them publicly.

The damage meter uses a dedicated damage revision, not every journal change. It counts
observed enemy HP loss, optionally enemy block, never overkill. Pet damage is credited
once to its owner. Missing owners stay unattributed. Percentages refer to attributed
damage only. Poison shares use the observed stack-contribution convention; this is not
claimed to be native individual ownership. One allocation supplies totals and trace,
including rounding remainders. Hiding F6 does not stop collection.

In **F5**, select a combat/round and cycle **Overview → Sources → Timeline**. Retained results
have a sequence, phase, dealer, target, source, HP/block/overkill and attribution on hover.
Trace limits are 2,048 results/combat, 8,192/run, plus a byte budget. Old trace pruning does
not change totals and is labeled. Existing archives have no invented retrospective trace.
Retry replaces the old combat branch. A native fatal-result flag does not rule out later
resurrection/death-prevention hooks.

HandViewer now uses unregistered presentation copies and native preview/description
methods. Preview writes target detached variables while calculations retain the original
card identity/owner. A cached reflection boundary strips event delegates from an unpublished
shell before native deep cloning: v111 clones enchanted/afflicted cards before clearing
copied event subscribers. Live subscriptions are untouched. Unreviewed external models
fail closed with an unavailable preview. Titles, costs and damage collection remain
independent. Previews have no selected target. Card nodes update in place; a description-only
change touches no Godot node properties. Forge/Replay notifications are observed.

Guardian separates value invalidation from structural reconciliation. Generic native
notifications no longer unbind every model. New lifecycle reactions default to uncertain
unless explicitly covered. Doom thresholds/chained death reactions, offensive/custom orbs,
Sly and other unsupported effects still produce partial forecasts. This update does not
execute native kill/end-turn/RNG commands or claim to simulate every card interaction.
Intent label updates use native `SetTextAutoSize`.

Both scripts run nine behavior suites, compile the mod, then check metadata/IL contracts
including visibility, return types, event signatures, preview clone boundaries and absence
of journal debug polling. These checks do not certify Godot rendering, host/client input,
multiplayer synchronization or FPS. Test in-game F3 targeting with enchanted cards, F5 across
victory/defeat/retry/reload, hidden F6 collection, two-player poison, Osty overflow, extra-turn
ready/undo and overlay scaling. Back up settings/journal before testing a new build.

'''+s[b:]
s=s.replace('- `tools/cli_reader.py` — optional assembly inspection utility.\n','').replace('Both scripts run seven C# behavior suites','Both scripts run nine C# behavior suites').replace('- `tests/` — six C# behavior projects,','- `tests/` — nine C# behavior projects,').replace('The behavior suites, v0.111 metadata contract, and mod compilation pass locally.', 'The build scripts verify the behavior suites, v0.111 metadata contract, and mod compilation.')
p.write_text(s)
