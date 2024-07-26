using BepInEx;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using UnityEngine;
using UnityEngine.UI;
using BepInEx.Configuration;
using static MC_SVShipRename.PersistentData;

namespace MC_SVShipRename
{
    [BepInPlugin(pluginGuid, pluginName, pluginVersion)]
    public class Main : BaseUnityPlugin
    {
        public const string pluginGuid = "mc.starvalor.shiprename";
        public const string pluginName = "SV Ship Rename";
        public const string pluginVersion = "1.0.0";

        private const string modSaveFolder = "/MCSVSaveData/";  // /SaveData/ sub folder
        private const string modSaveFilePrefix = "ShipNames_"; // modSaveFlePrefixNN.dat

        private const int dialogMode = 919;
        private const int playerIndex = -1;

        public enum FleetRenameMode { UseCrewName, UseShipName, Both };
        public static ConfigEntry<FleetRenameMode> cfg_FleetRename;

        private static PersistentData data;
        private static string tempName = null;

        private void Awake()
        {
            Harmony.CreateAndPatchAll(typeof(Main));

            cfg_FleetRename = Config.Bind<FleetRenameMode>("Config",
                "Fleet Ship Name Display Mode",
                FleetRenameMode.UseCrewName,
                "Change the name shown above fleet ships.  'UseCrewName' is vanilla and shows the captain's name.  'UseShipName' will use the ship's name if renamed with this mod.  'Both' will show both crew and ship name if renamed with this mod");
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.SelectItem))]
        [HarmonyPostfix]
        private static void InvSelectItem_Post(Inventory __instance, int itemIndex)
        {
            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(__instance);
            if (itemIndex < 0 || itemIndex >= cs.cargo.Count)
                return;

            if (cs.cargo[itemIndex].itemType != 4 |
                (int)AccessTools.Field(typeof(Inventory), "cargoMode").GetValue(__instance) == 2)
                return;

            GameObject btnRename = (GameObject)AccessTools.Field(typeof(Inventory), "btnRename").GetValue(__instance);
            if (btnRename == null)
                return;

            btnRename.GetComponentInChildren<Text>().text = Lang.Get(5, 319);
            btnRename.SetActive(true);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.ButtonRenamePress))]
        [HarmonyPostfix]
        private static void InvRenameBtnPress_Post(Inventory __instance)
        {
            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(__instance);
            int selectedItem = (int)AccessTools.Field(typeof(Inventory), "selectedItem").GetValue(__instance);

            if (selectedItem < 0 || selectedItem >= cs.cargo.Count || cs.cargo[selectedItem].itemType != 4 ||
                (int)AccessTools.Field(typeof(Inventory), "cargoMode").GetValue(__instance) == 2)
                return;

            InputDialog.inst.Open(dialogMode, selectedItem);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.AddToFleet))]
        [HarmonyPrefix]
        private static void InvAddToFleet_Pre(Inventory __instance)
        {
            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(__instance);
            int selectedItem = (int)AccessTools.Field(typeof(Inventory), "selectedItem").GetValue(__instance);

            if (data == null || selectedItem < 0 || selectedItem >= cs.cargo.Count || cs.cargo[selectedItem].itemType != 4)
                return;

            tempName = data.GetName(cs.cargo[selectedItem].shipLoadoutID, PersistentData.ID.IDType.Loadout);
        }

        [HarmonyPatch(typeof(GameManager), nameof(GameManager.CreatePlayerFleetMember))]
        [HarmonyPrefix]
        private static void GameManagerCreateFleety_Pre(CrewMember crewMember)
        {
            if (tempName == null)
                return;

            data.SetName(crewMember.id, PersistentData.ID.IDType.FleetCrewMember, tempName);
            tempName = null;
        }

        [HarmonyPatch(typeof(FleetControl), nameof(FleetControl.CleanFleetSlot))]
        [HarmonyPostfix]
        private static void FleetContCleaSlot_Post(AIMercenaryCharacter aiMercToRemove)
        {
            if (data == null || !(aiMercToRemove is PlayerFleetMember))
                return;

            PlayerFleetMember playerFleetMember = aiMercToRemove as PlayerFleetMember;

            string name;
            name = data.GetName(playerFleetMember.crewMemberID, PersistentData.ID.IDType.FleetCrewMember);
            if (name != null)
            {
                data.RemoveName(playerFleetMember.crewMemberID, PersistentData.ID.IDType.FleetCrewMember);
                data.SetName(GameData.data.shipLoadouts.Count > 0 ? GameData.data.shipLoadouts[GameData.data.shipLoadouts.Count - 1].id : 0, PersistentData.ID.IDType.Loadout, name);
            }
        }

        [HarmonyPatch(typeof(GameDataInfo), nameof(GameDataInfo.DeleteShipLoadout))]
        [HarmonyPostfix]
        private static void GameDataInfoDeleteLoadout_Post(int loadoutID)
        {
            data.RemoveName(loadoutID, PersistentData.ID.IDType.Loadout);
        }

        [HarmonyPatch(typeof(Inventory), nameof(Inventory.EquipSpaceShip))]
        [HarmonyPrefix]
        private static void InvEquipShip_Pre(Inventory __instance)
        {
            if (data == null)
                return;

            string curPlayerShipname = data.GetName(playerIndex, PersistentData.ID.IDType.Player);

            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(__instance);
            int selectedItem = (int)AccessTools.Field(typeof(Inventory), "selectedItem").GetValue(__instance);

            string newPlayerShipName = null;
            if (selectedItem > 0 && selectedItem < cs.cargo.Count)
                newPlayerShipName = data.GetName(cs.cargo[selectedItem].shipLoadoutID, PersistentData.ID.IDType.Loadout);

            if (curPlayerShipname != null)
            {
                data.SetName(cs.cargo[selectedItem].shipLoadoutID, PersistentData.ID.IDType.Loadout, curPlayerShipname);
                data.RemoveName(playerIndex, PersistentData.ID.IDType.Player);
            }
            if (newPlayerShipName != null)
            {
                data.SetName(playerIndex, PersistentData.ID.IDType.Player, newPlayerShipName);
                data.RemoveName(cs.cargo[selectedItem].shipLoadoutID, PersistentData.ID.IDType.Loadout);
            }
        }

        [HarmonyPatch(typeof(InputDialog), nameof(InputDialog.Open))]
        [HarmonyPrefix]
        private static bool InputOpen_Pre(InputDialog __instance, int newMode, int newVar1)
        {
            if (newMode != dialogMode)
                return true;

            Inventory inventory = (Inventory)AccessTools.Field(typeof(InputDialog), "inventory").GetValue(__instance);
            if (inventory == null)
                return false;

            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(inventory);
            if (cs == null || newVar1 >= cs.cargo.Count)
                return false;

            return true;
        }

        [HarmonyPatch(typeof(InputDialog), "ShowData")]
        [HarmonyPostfix]
        private static void InputShowData_Post(InputDialog __instance)
        {
            int mode = (int)AccessTools.Field(typeof(InputDialog), "mode").GetValue(__instance);
            if (mode != dialogMode)
                return;

            int var1 = (int)AccessTools.Field(typeof(InputDialog), "var1").GetValue(__instance);

            Inventory inventory = (Inventory)AccessTools.Field(typeof(InputDialog), "inventory").GetValue(__instance);
            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(inventory);
            CargoItem ci = cs.cargo[var1];
            if (ci.qnt > 1 || ci.itemType != 4 || GameData.data == null && GameData.data.shipLoadouts == null)
                return;

            string name = data.GetName(ci.shipLoadoutID, PersistentData.ID.IDType.Loadout);
            if (name == null)
            {
                SpaceShipData shipData = GameData.data.GetShipLoadout(ci.shipLoadoutID);
                name = ShipDB.GetModel(shipData.shipModelID).modelName;
            }

            InputField inputfield = (InputField)AccessTools.Field(typeof(InputDialog), "textInput").GetValue(__instance);
            inputfield.text = name;
            Text title = (Text)AccessTools.Field(typeof(InputDialog), "titleText").GetValue(__instance);
            title.text = Lang.Get(5, 320, "<b>" + name + "</b>");
        }

        [HarmonyPatch(typeof(InputDialog), nameof(InputDialog.Execute))]
        [HarmonyPostfix]
        private static void InputExecute_Post(InputDialog __instance)
        {
            int mode = (int)AccessTools.Field(typeof(InputDialog), "mode").GetValue(__instance);
            if (mode != dialogMode)
                return;

            int var1 = (int)AccessTools.Field(typeof(InputDialog), "var1").GetValue(__instance);

            Inventory inventory = (Inventory)AccessTools.Field(typeof(InputDialog), "inventory").GetValue(__instance);
            CargoSystem cs = (CargoSystem)AccessTools.Field(typeof(Inventory), "cs").GetValue(inventory);
            CargoItem ci = cs.cargo[var1];
            if (ci.qnt > 1 || ci.itemType != 4 || GameData.data == null && GameData.data.shipLoadouts == null)
                return;

            SpaceShipData shipData = GameData.data.GetShipLoadout(ci.shipLoadoutID);
            if (shipData == null)
                return;

            InputField inputfield = (InputField)AccessTools.Field(typeof(InputDialog), "textInput").GetValue(__instance);
            if (String.IsNullOrEmpty(inputfield.text))
                return;

            if (data == null)
                data = new PersistentData();

            data.SetName(ci.shipLoadoutID, PersistentData.ID.IDType.Loadout, inputfield.text);
            inventory.RefreshIfOpen(null, true, true);
        }

        [HarmonyPatch(typeof(ShipInfo), nameof(ShipInfo.LoadData))]
        [HarmonyPostfix]
        private static void ShipInfoLoadData_Post(ShipInfo __instance)
        {
            if (data == null)
                return;

            SpaceShip ss = (SpaceShip)AccessTools.Field(typeof(ShipInfo), "ss").GetValue(__instance);
            if (ss == null || ss.shipData == null)
                return;

            string name = null;
            if (ss.IsPlayer)
                name = data.GetName(playerIndex, PersistentData.ID.IDType.Player);
            else
            {
                AIControl aic = ss.gameObject.GetComponent<AIControl>();
                if (aic != null && aic.Char is PlayerFleetMember)
                {
                    PlayerFleetMember fleety = aic.Char as PlayerFleetMember;
                    name = data.GetName(fleety.crewMemberID, PersistentData.ID.IDType.FleetCrewMember);
                }
            }

            if (name == null)
                return;

            string fleetShipText = "";
            if (__instance.editingFleetShip != null)
            {
                fleetShipText = fleetShipText + " <b>(" + __instance.editingFleetShip.CommanderName(12) + ")</b>";
            }

            ((Text)AccessTools.Field(typeof(ShipInfo), "shipModelName").GetValue(__instance)).text =
                ItemDB.GetRarityColor(ss.stats.modelData.rarity)
                + "<b>" + name + "</b></color>" + fleetShipText;
        }

        [HarmonyPatch(typeof(CargoItem), nameof(CargoItem.GetNameWithQnt))]
        [HarmonyPostfix]
        private static void CargoItemGetNameWithQnt_Post(CargoItem __instance, ref string __result)
        {
            if (data == null || __instance.itemType != 4 ||
                GameData.data == null && GameData.data.shipLoadouts == null)
                return;

            string name = data.GetName(__instance.shipLoadoutID, PersistentData.ID.IDType.Loadout);
            if (name == null)
                return;

            string formatting = "<size=14>" + ItemDB.GetRarityColor(ShipDB.GetModel(__instance.itemID).rarity);

            __result = formatting + name + "</color></size>";
        }

        [HarmonyPatch(typeof(HPBarControl), nameof(HPBarControl.SetName))]
        [HarmonyPostfix]
        private static void HPBarSetName_Post(HPBarControl __instance)
        {
            if (data == null || cfg_FleetRename.Value == FleetRenameMode.UseCrewName)
                return;

            if (__instance.owner.CompareTag("NPC"))
            {
                AIControl aic = __instance.owner.GetComponent<AIControl>();
                if (aic != null && !(aic.Char is PlayerFleetMember))
                    return;

                PlayerFleetMember fleety = aic.Char as PlayerFleetMember;

                string name = null;
                name = data.GetName(fleety.crewMemberID, PersistentData.ID.IDType.FleetCrewMember);
                if (name == null)
                    return;

                if (cfg_FleetRename.Value == FleetRenameMode.Both)
                    name += " (" + fleety.name + ")";
                
                ((Text)AccessTools.Field(typeof(HPBarControl), "textName").GetValue(__instance)).text = name + " [" + aic.Char.level + "]";
            }
        }

        [HarmonyPatch(typeof(MenuControl), nameof(MenuControl.LoadGame))]
        [HarmonyPostfix]
        private static void MenuControlLoadGame_Post()
        {
            LoadData(GameData.gameFileIndex.ToString("00"));
        }

        internal static void LoadData(string saveIndex)
        {
            string modData = Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + saveIndex + ".dat";
            try
            {
                if (!saveIndex.IsNullOrWhiteSpace() && File.Exists(modData))
                {
                    BinaryFormatter binaryFormatter = new BinaryFormatter();
                    FileStream fileStream = File.Open(modData, FileMode.Open);
                    PersistentData loadData = (PersistentData)binaryFormatter.Deserialize(fileStream);
                    fileStream.Close();

                    if (loadData == null)
                        data = new PersistentData();
                    else
                        data = loadData;
                }
                else
                    data = new PersistentData();
            }
            catch
            {
                SideInfo.AddMsg("<color=red>Ship rename mod load failed.</color>");
            }
        }

        [HarmonyPatch(typeof(GameData), nameof(GameData.SaveGame))]
        [HarmonyPrefix]
        private static void GameDataSaveGame_Pre()
        {
            SaveData();
        }

        private static void SaveData()
        {
            if (data == null)
                return;

            string tempPath = Application.dataPath + GameData.saveFolderName + modSaveFolder + "RSTemp.dat";

            if (!Directory.Exists(Path.GetDirectoryName(tempPath)))
                Directory.CreateDirectory(Path.GetDirectoryName(tempPath));

            if (File.Exists(tempPath))
                File.Delete(tempPath);

            BinaryFormatter binaryFormatter = new BinaryFormatter();
            FileStream fileStream = File.Create(tempPath);
            binaryFormatter.Serialize(fileStream, data);
            fileStream.Close();

            File.Copy(tempPath, Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + GameData.gameFileIndex.ToString("00") + ".dat", true);
            File.Delete(tempPath);
        }

        [HarmonyPatch(typeof(MenuControl), nameof(MenuControl.DeleteSaveGame))]
        [HarmonyPrefix]
        private static void DeleteSave_Pre()
        {
            if (GameData.ExistsAnySaveFile(GameData.gameFileIndex) &&
                File.Exists(Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + GameData.gameFileIndex.ToString("00") + ".dat"))
            {
                File.Delete(Application.dataPath + GameData.saveFolderName + modSaveFolder + modSaveFilePrefix + GameData.gameFileIndex.ToString("00") + ".dat");
            }
        }
    }

    [Serializable]
    public class PersistentData
    {
        private Dictionary<ID, string> shipNames;

        public PersistentData()
        {
            shipNames = new Dictionary<ID, string>();
        }

        public string GetName(int index, ID.IDType type)
        {
            foreach (ID id in shipNames.Keys)
                if (id.index == index && id.type == type)
                    return shipNames[id];

            return null;
        }

        public void SetName(int index, ID.IDType type, string name)
        {
            if (shipNames == null)
                shipNames = new Dictionary<ID, string>();

            ID foundID = null;
            foreach (ID id in shipNames.Keys)
                if (id.index == index && id.type == type)
                    foundID = id;

            if (foundID != null)
                shipNames[foundID] = name;
            else
                shipNames.Add(new ID(index, type), name);
        }

        public void RemoveName(int index, ID.IDType type)
        {
            ID foundID = null;
            foreach (ID id in shipNames.Keys)
            {
                if (id.index == index && id.type == type)
                {
                    foundID = id;
                    break;
                }
            }

            shipNames.Remove(foundID);
        }

        [Serializable]
        public class ID
        {
            public enum IDType { Loadout, FleetCrewMember, Player }
            public int index;
            public IDType type;

            public ID(int index, IDType type)
            {
                this.index = index;
                this.type = type;
            }
        }
    }
}
