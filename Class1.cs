using BepInEx;
//using R2API.Utils;
using RoR2;
using RoR2.Artifacts;
using RoR2.ConVar;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine;
using static RoR2.GenericPickupController;

namespace EBKPlugin
{
    [BepInPlugin(PluginGUID, PluginName, PluginVersion)]
    //   [BepInDependency("com.bepis.r2api")]
    //   [NetworkCompatibility(CompatibilityLevel.NoNeedForSync)]
    //   [R2APISubmoduleDependency(nameof(CommandHelper))]

    public class EBKPlugin : BaseUnityPlugin
    {
        internal protected const string PluginGUID = PluginAuthor + "." + PluginName;
        internal protected const string PluginAuthor = "EBK21";
        internal protected const string PluginName = "EBKPlugin";
        internal protected const string PluginVersion = "1.2.9";
        private protected readonly List<GameObject> pObjs = new List<GameObject>();
        private static readonly Queue<Assembly> Assemblies = new Queue<Assembly>();
        private protected static RoR2.Console _console;
        private protected static EBKPlugin instance;
        private static readonly System.Random rng = new System.Random();
        EBKPlugin()
        {
            instance = this;
        }
        private static void Send(string? message)
        {
            Chat.SendBroadcastChat(new Chat.SimpleChatMessage
            {
                baseToken = "{0}",
                paramTokens = new[] { message }
            });
        }
        private static void RegisterCommands(Assembly assembly)
        {
            var types = assembly?.GetTypes();
            if (types == null)
            {
                return;
            }

            try
            {
                var catalog = (Dictionary<string, RoR2.Console.ConCommand>)typeof(RoR2.Console).GetField("concommandCatalog", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).GetValue(_console);
                const BindingFlags consoleCommandsMethodFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                var methods = types.SelectMany(t =>
                    t.GetMethods(consoleCommandsMethodFlags).Where(m => m.GetCustomAttribute<ConCommandAttribute>() != null));

                foreach (var methodInfo in methods)
                {
                    if (!methodInfo.IsStatic)
                    {
                        UnityEngine.Debug.LogError($"ConCommand defined as {methodInfo.Name} in {assembly.FullName} could not be registered. " +
                                              "ConCommands must be static methods.");
                        continue;
                    }

                    var attributes = methodInfo.GetCustomAttributes<ConCommandAttribute>();
                    var a = _console.GetType().GetNestedType("ConCommand", consoleCommandsMethodFlags);

                    foreach (var attribute in attributes)
                    {
                        object instance = Activator.CreateInstance(a);
                        a.GetField("flags", consoleCommandsMethodFlags).SetValue(instance, attribute.flags);
                        a.GetField("helpText", consoleCommandsMethodFlags).SetValue(instance, attribute.helpText);
                        a.GetField("action", consoleCommandsMethodFlags).SetValue(instance, (RoR2.Console.ConCommandDelegate)Delegate.CreateDelegate(
                                typeof(RoR2.Console.ConCommandDelegate), methodInfo));

                        catalog[attribute.commandName.ToLower()] = (RoR2.Console.ConCommand)instance;
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($" failed to scan the assembly called {assembly.FullName}. Exception : {e}");
            }
        }
        private static void RegisterConVars(Assembly assembly)
        {
            try
            {
                var customVars = new List<BaseConVar>();
                foreach (var type in assembly.GetTypes())
                {
                    foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (field.FieldType.IsSubclassOf(typeof(BaseConVar)))
                        {
                            if (field.IsStatic)
                            {
                                typeof(RoR2.Console).GetMethod("RegisterConVarInternal", BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Invoke(_console, new object[] { (BaseConVar)field.GetValue(null) });
                                customVars.Add((BaseConVar)field.GetValue(null));
                            }
                            else if (type.GetCustomAttribute<CompilerGeneratedAttribute>() == null)
                            {
                                UnityEngine.Debug.LogError(
                                    $"ConVar defined as {type.Name} in {assembly.FullName}. {field.Name} could not be registered. ConVars must be static fields.");
                            }
                        }
                    }
                }
                foreach (var baseConVar in customVars)
                {
                    if ((baseConVar.flags & ConVarFlags.Engine) != ConVarFlags.None)
                    {
                        baseConVar.defaultValue = baseConVar.GetString();
                    }
                    else if (baseConVar.defaultValue != null)
                    {
                        baseConVar.SetString(baseConVar.defaultValue);
                    }
                }
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogError($"failed to scan the assembly called {assembly.FullName}. Exception : {e}");
            }
        }
        private static void ConsoleReady(On.RoR2.Console.orig_InitConVars orig, RoR2.Console self)
        {
            orig(self);

            _console = self;
            HandleCommandsConvars();
        }
        private static void AddToConsoleWhenReady()
        {

            var assembly = Assembly.GetCallingAssembly();
            Assemblies.Enqueue(assembly);
            HandleCommandsConvars();
        }
        private static void HandleCommandsConvars()
        {
            if (_console == null)
            {
                return;
            }

            while (Assemblies.Count > 0)
            {
                var assembly = Assemblies.Dequeue();
                RegisterCommands(assembly);
                RegisterConVars(assembly);
            }
        }
        public void Awake()
        {
            //            CommandHelper.AddToConsoleWhenReady();
            On.RoR2.Console.InitConVars += ConsoleReady;
            On.SteamAPIValidator.SteamApiValidator.IsValidSteamApiDll += (orig) =>
            {
                return true;
            };
            AddToConsoleWhenReady();
            On.RoR2.ShrineBossBehavior.Start += (orig, self) =>
            {
                self.maxPurchaseCount = 3;
                orig(self);
            };
            On.RoR2.Util.GetExpAdjustedDropChancePercent += (orig, b, c) =>
            {
                float f1 = 0f;
                switch (Run.instance.participatingPlayerCount)
                {
                    case 1:
                        f1 += 5f;
                        break;
                    case 2:
                        f1 += 3f;
                        break;
                    default:
                        f1 += 1f;
                        break;
                }
                return orig(b, c) + f1;
            };
            On.UnityEngine.Networking.NetworkServer.Spawn_GameObject += (orig, obj) =>
            {
                orig(obj);
                if (obj.name.Contains("CommandCube") || obj.name.Contains("PickupDroplet") || obj.name.Contains("GenericPickup"))
                    instance.pObjs.Add(obj);

            };
            On.UnityEngine.Networking.NetworkServer.Destroy += (orig, obj) =>
            {
                orig(obj);
                pObjs.Remove(obj);
            };
            On.RoR2.Run.BeginStage += (orig, self) =>
            {
                orig(self);
                Send("Clean recorded pickup list... count=" + instance.pObjs.Count);
                instance.pObjs.Clear();
            };
            On.RoR2.Run.RecalculateDifficultyCoefficentInternal += (orig, self) =>
            {
                orig(self);
                switch (Run.instance.stageClearCount)
                {
                    case 0:
                        break;
                    case 1:
                        Run.instance.compensatedDifficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.06f;
                        Run.instance.difficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.08f;
                        break;
                    case 2:
                        Run.instance.compensatedDifficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.09f;
                        Run.instance.difficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.11f;
                        break;
                    case 3:
                        Run.instance.compensatedDifficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.13f;
                        Run.instance.difficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.15f;
                        break;
                    case 4:
                        Run.instance.compensatedDifficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.18f;
                        Run.instance.difficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.20f;
                        break;
                    case 5:
                        Run.instance.compensatedDifficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.24f;
                        Run.instance.difficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * 1.26f;
                        break;
                    default:
                        Run.instance.compensatedDifficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * (1.26f + 0.03f * (Run.instance.stageClearCount - 5));
                        Run.instance.difficultyCoefficient = Run.instance.compensatedDifficultyCoefficient * (1.28f + 0.03f * (Run.instance.stageClearCount - 5));
                        break;

                }
            };
            On.RoR2.Run.OnEnable += (orig, self) =>
            {
                typeof(Run).GetField("ambientLevelCap", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).SetValue(self, 356);
                orig(self);
            };
            On.RoR2.Artifacts.SacrificeArtifactManager.OnPrePopulateSceneServer += (orig, s) =>
            {
                UnityEngine.Debug.Log("Nanana");
            };
            On.RoR2.PickupDropletController.CreatePickupDroplet_PickupIndex_Vector3_Vector3 += (orig, pick, pos, vel) =>
            {
                orig(pick, pos, vel);
                int num = Run.instance.participatingPlayerCount;
                if (num >= 4)
                    num -= 1;
                var stackTrace = new StackTrace(1, false);
                var st = stackTrace.ToString();
                if (st.Contains("ScavBackpackBehavior") || st.Contains("create_droplets"))
                {
                    goto exit;
                }
                if (st.Contains("BossGroup"))
                {
                    var t = ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(pick).itemIndex).tier;
                    if (t == ItemTier.Tier3 || t == ItemTier.Boss || t == ItemTier.VoidTier3)
                        goto exit;
                }
                if (st.Contains("ScrappingToIdle") || st.Contains("Duplicating"))
                {
                    num = 1;
                }
                for (int i = 1; i <= num; i++)
                {
                    Vector3 v = new Vector3(2 + 2 * i, 2 + 2 * i, 0);
                    orig(pick, pos + v, vel);
                }
            exit:;
            };
            On.RoR2.PickupDropletController.CreatePickupDroplet_CreatePickupInfo_Vector3_Vector3 += (orig, pi, v1, v2) =>
            {
                orig(pi, v1, v2);
                var stackTrace = new StackTrace(1, false);
                var st = stackTrace.ToString();
                if (st.Contains("PickupIndex"))
                    goto exit;
                int num = Run.instance.participatingPlayerCount;
                if (num >= 4)
                    num -= 1;
                var t = ItemTier.NoTier;
                try { t = ItemCatalog.GetItemDef(PickupCatalog.GetPickupDef(pi.pickupIndex).itemIndex).tier; } catch (NullReferenceException) { };
                if (st.Contains("ArenaMissionController") || st.Contains("InfiniteTowerWaveController"))
                {
                    if (t == ItemTier.Tier3 || t == ItemTier.Boss || t == ItemTier.VoidTier3)
                        goto exit;
                }
                for (int i = 1; i <= num; i++)
                {
                    Vector3 v = new Vector3(2 + 2 * i, 2 + 2 * i, 0);
                    orig(pi, v1 + v, v2);
                }
            exit:;
            };
            On.RoR2.GenericPickupController.AttemptGrant += (orig, self, b) =>
            {
                orig(self, b);
                var pickupDef = PickupCatalog.GetPickupDef(self.pickupIndex);
                if (pickupDef.coinValue != 0U)
                    foreach (var n in NetworkUser.readOnlyInstancesList)
                    {
                        Send("Also given lunar coin to " + n.userName);
                        n.AwardLunarCoins(pickupDef.coinValue);
                    }

            };
            On.RoR2.SceneDirector.PopulateScene += (orig, self) =>
            {
                self.interactableCredit = (int)(self.interactableCredit * 1.77);
                orig(self);

            };
            On.RoR2.UI.LogBook.PageBuilder.AddSimplePickup += (orig, self, index) =>
            {
                orig(self, index);
                self.AddNotesPanel("Index: " + index.value);
            };
            On.RoR2.GlobalEventManager.OnPlayerCharacterDeath += (orig, self, dmg, user) =>
            {
                orig(self, dmg, user);
                var pname = user.userName;
                var mname = "环境,负面效果或其他";
                try
                {
                    mname = Language.GetString(BodyCatalog.GetBodyPrefab(dmg.attackerBodyIndex).GetComponent<CharacterBody>().baseNameToken);
                }
                catch (NullReferenceException) { };
                Send(pname + " is killed by " + mname);
            };
            On.RoR2.Inventory.GiveItem_ItemIndex_int += (orig, self, ii, i) =>
            {
                orig(self, ii, i);
                var stackTrace = new StackTrace(1, false);
                var st = stackTrace.ToString();
                if (st.Contains("give_monster_item"))
                {
                    goto exit;
                }
                if (st.Contains("GrantMonsterTeamItem"))
                {
                    var mul = 1;
                    if (Run.instance.stageClearCount >= 5)
                    {
                        mul = Run.instance.stageClearCount/3;
                    }
                    else if (Run.instance.stageClearCount >= 4)
                        mul = 2;
                    if (Run.instance.stageClearCount >= 1)
                    {
                        switch (ItemCatalog.GetItemDef(ii).tier)
                        {
                            case ItemTier.Tier3:
                                orig(self, ii, rng.Next(0, 1) * mul);
                                break;
                            case ItemTier.Tier2:
                                orig(self, ii, rng.Next(1, 4) * mul);
                                break;
                            case ItemTier.Tier1:
                                orig(self, ii, rng.Next(1, 10) * mul);
                                break;
                        }
                    }
                }
            exit:;
            };
            UnityEngine.Debug.Log("EBK's interesting mod L-O-A-D-E--D ! Hope you happy now~");
        }

        [ConCommand(commandName = "respawn_dead", flags = ConVarFlags.ExecuteOnServer, helpText = "Respawn dead players")]
        private static void respawn_dead(ConCommandArgs args)
        {
            foreach (var networkUser in NetworkUser.readOnlyInstancesList)
            {
                if (networkUser.master.IsDeadAndOutOfLivesServer())
                {
                    Send(networkUser.userName + " sucks, respawn it ...");
                    networkUser.master.RespawnExtraLife();
                }
            }
        }
        [ConCommand(commandName = "create_droplets", flags = ConVarFlags.ExecuteOnServer, helpText = "Create Droplets")]
        private static void create_droplets(ConCommandArgs args)
        {
            bool showerror = false;
            int a = -1;
            int b = -1;
            int c = 0;
            switch (args.Count)
            {
                case 3:
                    int.TryParse(args[2], out c);
                    goto case 2;
                case 2:
                    int.TryParse(args[1], out b);
                    goto case 1;
                case 1:
                    int.TryParse(args[0], out a);
                    break;
                default:
                    goto bye;

            }
            if (a < 0)
                return;
            if (b <= 0)
                b = 1;
            if (c < 0)
                c = 0;
            if (c > 1)
                c = 1;
            var p = new PickupIndex(a);
            var sender = args.GetSenderMaster();
            var dl = sender.GetBody().footPosition;
            if (dl != null && p != null)
            {
                Send("Creating " + b + " " + Language.GetString(PickupCatalog.GetPickupDef(p).nameToken) + " at " + dl.x + "," + dl.y + "," + dl.z);
                for (int i = 0; i < b; i++)
                {
                    switch (c)
                    {
                        default:
                        case 0:
                            try { PickupDropletController.CreatePickupDroplet(p, dl + new Vector3(2 + 2 * i, 2 + 2 * i, 0), Vector3.up); } catch { goto bye; };
                            break;
                        case 1:
                            CreatePickupInfo createPickupInfo = new CreatePickupInfo();
                            createPickupInfo.position = dl + new Vector3(2 + 2 * i, 2 + 2 * i, 0);
                            createPickupInfo.rotation = Quaternion.identity;
                            createPickupInfo.pickupIndex = p;
                            try { CreatePickup(createPickupInfo); } catch { goto bye; };
                            break;
                    }
                }
            }
        bye:
            if (!showerror)
            {
                showerror = true;
                UnityEngine.Debug.Log("Wrong pick up");
            };
        }
        [ConCommand(commandName = "list_player_index", flags = ConVarFlags.None, helpText = "Show players' index")]
        private static void list_player_index(ConCommandArgs args)
        {
            var s = new StringBuilder();
            foreach (NetworkUser n in NetworkUser.readOnlyInstancesList)
            {
                s.AppendLine(n.userName + ":" + NetworkUser.readOnlyInstancesList.IndexOf(n));
            }
            UnityEngine.Debug.Log(s.ToString());
        }
        [ConCommand(commandName = "give_player_item", flags = ConVarFlags.ExecuteOnServer, helpText = "Give player item")]
        private static void give_player_item(ConCommandArgs args)
        {
            NetworkUser? user = null;
            int a = -1;
            int b = -1;
            int c = 1;
            switch (args.Count)
            {
                case 3:
                    int.TryParse(args[2], out c);
                    goto case 2;
                case 2:
                    int.TryParse(args[1], out b);
                    goto case 1;
                case 1:
                    int.TryParse(args[0], out a);
                    break;
                default:
                    return;

            }
            if (a < 0)
                return;
            if (b < 0)
                return;
            if (c <= 0)
                c = 1;
            var p = new PickupIndex(a);
            var pickupDef = PickupCatalog.GetPickupDef(p);
            try { user = NetworkUser.readOnlyInstancesList[b]; } catch (ArgumentOutOfRangeException) { };
            if (user != null && pickupDef != null)
            {
                Send("Giving " + c + " " + Language.GetString(pickupDef.nameToken) + " to " + user.userName);
                user.master.GetBody().inventory.GiveItem(pickupDef.itemIndex, c);
            }
        }
        [ConCommand(commandName = "give_monster_item", flags = ConVarFlags.ExecuteOnServer, helpText = "Give monster item")]
        private static void give_monster_item(ConCommandArgs args)
        {
            int a = -1;
            int b = -1;
            int.TryParse(args[0], out a);
            int.TryParse(args[1], out b);
            if (a < 0)
                return;
            if (b <= 0)
                b = 1;
            Inventory minv = (Inventory)typeof(MonsterTeamGainsItemsArtifactManager).GetField("monsterTeamInventory",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            var p = new PickupIndex(a+9);
            var pickupDef = PickupCatalog.GetPickupDef(p);
            if (pickupDef != null)
            {
                minv.GiveItem(pickupDef.itemIndex, b);
            };
        }
        [ConCommand(commandName = "remove_monster_item", flags = ConVarFlags.ExecuteOnServer, helpText = "Remove monster item")]
        private static void remove_monster_item(ConCommandArgs args)
        {
            int a = -1;
            int b = -1;
            int.TryParse(args[0], out a);
            int.TryParse(args[1], out b);
            if (a < 0)
                return;
            if (b <= 0)
                b = 1;
            Inventory minv = (Inventory)typeof(MonsterTeamGainsItemsArtifactManager).GetField("monsterTeamInventory", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var p = new PickupIndex(a+9);
            var pickupDef = PickupCatalog.GetPickupDef(p);
            if (pickupDef != null)
            {
                minv.RemoveItem(ItemCatalog.GetItemDef(pickupDef.itemIndex), b);
            };
        }
        [ConCommand(commandName = "remove_player_item", flags = ConVarFlags.ExecuteOnServer, helpText = "Remove player item")]
        private static void remove_player_item(ConCommandArgs args)
        {
            NetworkUser? user = null;
            int a = -1;
            int b = -1;
            int c = 1;
            switch (args.Count)
            {
                case 3:
                    int.TryParse(args[2], out c);
                    goto case 2;
                case 2:
                    int.TryParse(args[1], out b);
                    goto case 1;
                case 1:
                    int.TryParse(args[0], out a);
                    break;
                default:
                    return;

            }
            if (a < 0)
                return;
            if (b < 0)
                return;
            if (c <= 0)
                c = 1;
            var p = new PickupIndex(a);
            var pickupDef = PickupCatalog.GetPickupDef(p);
            try { user = NetworkUser.readOnlyInstancesList[b]; } catch (ArgumentOutOfRangeException) { };
            if (user != null && pickupDef != null)
            {
                Send("Giving " + c + " " + Language.GetString(pickupDef.nameToken) + " to " + user.userName);
                user.master.GetBody().inventory.RemoveItem(pickupDef.itemIndex, c);
            }
        }
        [ConCommand(commandName = "item_info", flags = ConVarFlags.None, helpText = "Show item index")]
        private static void itemindex(ConCommandArgs args)
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("Pos|Index|CodeName|RealName|Tier|Tags");
            int i = 0;
            foreach (var item in (ItemDef[])(typeof(ItemCatalog).GetField("itemDefs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).GetValue(null)))
            {

                stringBuilder.Append(i++ + "|");
                stringBuilder.Append(item.name + "|");
                stringBuilder.Append(PickupCatalog.FindPickupIndex(item.itemIndex).value.ToString() + "|");
                stringBuilder.Append(Language.GetString(item.nameToken) + "|");
                
                string tierString = "No Tier";
                switch (ItemCatalog.GetItemDef(item.itemIndex).tier)
                {
                    case ItemTier.Boss:
                        tierString = "Boss";
                        break;
                    case ItemTier.Lunar:
                        tierString = "Lunar";
                        break;
                    case ItemTier.Tier1:
                        tierString = "White";
                        break;
                    case ItemTier.Tier2:
                        tierString = "Green";
                        break;
                    case ItemTier.Tier3:
                        tierString = "Red";
                        break;
                    case ItemTier.VoidBoss:
                        tierString = "Boss Void";
                        break;
                    case ItemTier.VoidTier1:
                        tierString = "White Void";
                        break;
                    case ItemTier.VoidTier2:
                        tierString = "Green Void";
                        break;
                    case ItemTier.VoidTier3:
                        tierString = "Red Void";
                        break;
                }
                stringBuilder.Append(tierString + "|");

                for (int j = 0; j < item.tags.Length; j++)
                {
                    var _tag = item.tags[j];

                    stringBuilder.Append(_tag.ToString());
                    if (j < item.tags.Length - 1)
                        stringBuilder.Append("_");
                }
                stringBuilder.AppendLine("");
            }
            stringBuilder.AppendLine("--");

            stringBuilder.AppendLine("Pos|Index|CodeName|RealName|Tier|Tags|PickIndex");

            i = 0;
            foreach (var item2 in (EquipmentDef[])(typeof(EquipmentCatalog).GetField("equipmentDefs", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).GetValue(null)))
            {

                stringBuilder.Append(i++ + "|");
                stringBuilder.Append(item2.name + "|");
                stringBuilder.Append(item2.equipmentIndex.ToString() + "|");
                stringBuilder.Append(Language.GetString(item2.nameToken) + "|");
                stringBuilder.Append((item2.isLunar ? "Lunar" : "Normal") + "|");
                stringBuilder.Append(PickupCatalog.FindPickupIndex(item2.equipmentIndex).value.ToString());
                stringBuilder.AppendLine("");
            }
            UnityEngine.Debug.Log(stringBuilder.ToString());
        }
        [ConCommand(commandName = "pickup_info", flags = ConVarFlags.None, helpText = "Show pickup index")]
        private static void pickindex(ConCommandArgs args)
        {
            var sb = new StringBuilder();
            foreach (var pi in (PickupDef[])typeof(PickupCatalog).GetField("entries", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).GetValue(null))
            {
                sb.Append(pi.internalName + "|");
                sb.Append(pi.pickupIndex.value.ToString());
                sb.AppendLine("");
            }
            UnityEngine.Debug.Log(sb.ToString());
        }
        [ConCommand(commandName = "clean_pickups", flags = ConVarFlags.ExecuteOnServer, helpText = "Clean all dropped pickups")]
        private static void clean_pickups(ConCommandArgs args)
        {
            List<GameObject> s = new List<GameObject>(instance.pObjs);
            instance.pObjs.Clear();
            Send("Destroying " + s.Count + " recorded pickups ...");
            if (s.Count > 0)
            {
                foreach (var go in s)
                {
                    UnityEngine.Networking.NetworkServer.Destroy(go);
                };
            }
            s.Clear();
            Send("Done clean pickups");
        }
    }
}

