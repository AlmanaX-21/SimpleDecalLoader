using System;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.UI;
using HarmonyLib;
using System.IO;
using System.Collections.Generic;
using UnityEngine.EventSystems;

namespace SimpleDecalLoader
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class MainPlugin : BaseUnityPlugin
    {
        public const string ModGUID = "me.almana.simpledecal";
        public const string ModName = "SimpleDecalLoader";
        public const string ModVersion = "1.1.0";
        public const string ModAuthor = "AlmanaX21";

        internal static ManualLogSource Log;
        internal static List<GameObject> customButtons = new List<GameObject>();

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"Plugin {ModName} is loading...");

            Harmony.CreateAndPatchAll(typeof(MainPlugin).Assembly);

            Log.LogInfo($"Mod ID: {ModGUID}");
            Log.LogInfo($"Version: {ModVersion}");
            Log.LogInfo($"Author: {ModAuthor}");
            Log.LogInfo($"Plugin {ModName} is loaded!");
        }

        [HarmonyPatch(typeof(UnityEngine.UI.Button), "OnPointerClick")]
        public static class ButtonClickPatch
        {
            [HarmonyPostfix]
            public static void Postfix(UnityEngine.UI.Button __instance)
            {
                if (__instance != null)
                {
                    Log.LogInfo($"Button clicked: {__instance.name} (GameObj: {__instance.gameObject.name})");
                }
            }
        }

        [HarmonyPatch(typeof(DecalButtonPanel), "Start")]
        public static class DecalPanelPatch
        {
            [HarmonyPostfix]
            public static void Postfix(DecalButtonPanel __instance)
            {
                LoadCustomDecals(__instance);
            }
        }

        public static void LoadCustomDecals(DecalButtonPanel panel)
        {
            foreach (var btn in customButtons)
            {
                if (btn != null) Destroy(btn);
            }
            customButtons.Clear();

            string decalsPath = Path.Combine(Paths.ConfigPath, "decals");
            if (!Directory.Exists(decalsPath)) Directory.CreateDirectory(decalsPath);

            Log.LogInfo($"Loading decals from {decalsPath}...");

            string[] files = Directory.GetFiles(decalsPath, "*.png");
            foreach (string file in files)
            {
                if (Path.GetFileName(file) == "reload_icon.png") continue;
                Texture2D tex = LoadTexture(file);
                if (tex != null)
                {
                    GameObject newBtn = Instantiate(panel.decalButtonPrefab, panel.transform);
                    DecalButton db = newBtn.GetComponent<DecalButton>();
                    if (db != null)
                    {
                        db.Init(tex, panel.placer);
                        db.name = "CustomDecal_" + Path.GetFileNameWithoutExtension(file);
                        customButtons.Add(newBtn);
                    }
                }
            }

            CreateReloadButton(panel);
        }

        private static void CreateReloadButton(DecalButtonPanel panel)
        {
            GameObject reloadBtn = Instantiate(panel.decalButtonPrefab, panel.transform);
            reloadBtn.name = "ReloadDecalsButton";

            DecalButton db = reloadBtn.GetComponent<DecalButton>();
            if (db != null) Destroy(db);

            foreach (Transform child in reloadBtn.transform)
            {
                if (child.GetComponent<Text>() == null)
                    Destroy(child.gameObject);
            }

            Button btnComp = reloadBtn.GetComponent<Button>();
            if (btnComp == null) btnComp = reloadBtn.AddComponent<Button>();

            btnComp.onClick.RemoveAllListeners();
            btnComp.onClick.AddListener(() =>
            {
                Log.LogInfo("Reloading decals...");
                LoadCustomDecals(panel);
            });

            Image img = reloadBtn.GetComponent<Image>();
            if (img != null)
            {
                string iconPath = Path.Combine(Paths.ConfigPath, "decals", "reload_icon.png");
                Texture2D iconTex = null;

                // 1. Try to load from disk
                if (File.Exists(iconPath))
                {
                    iconTex = LoadTexture(iconPath);
                }

                // 2. If not on disk, load from embedded and save to disk
                if (iconTex == null)
                {
                    iconTex = LoadEmbeddedTexture("reload_icon.png");
                    if (iconTex != null)
                    {
                        // Extract to disk so user can see/customize it
                        try
                        {
                            // Ensure directory exists
                            string dir = Path.GetDirectoryName(iconPath);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                            byte[] pngData = iconTex.EncodeToPNG();
                            File.WriteAllBytes(iconPath, pngData);
                            Log.LogInfo($"Extracted default reload_icon.png to {iconPath}");
                        }
                        catch (Exception ex)
                        {
                            Log.LogError($"Failed to extract reload_icon.png: {ex.Message}");
                        }
                    }
                }

                if (iconTex != null)
                {
                    img.sprite = Sprite.Create(iconTex, new Rect(0, 0, iconTex.width, iconTex.height), new Vector2(0.5f, 0.5f));
                    img.color = Color.white;
                }
                else
                {
                    img.color = Color.red;
                }
            }
            Text txt = reloadBtn.GetComponentInChildren<Text>();
            if (txt != null) txt.text = "Reload";

            SimpleTooltip tooltip = reloadBtn.AddComponent<SimpleTooltip>();
            tooltip.tooltipText = "Reload Decals";

            customButtons.Add(reloadBtn);
        }

        public static Texture2D LoadEmbeddedTexture(string resourceName)
        {
            var assembly = System.Reflection.Assembly.GetExecutingAssembly();

            // Find resource that ends with the requested name to be more robust against namespace changes
            string fullResourceName = System.Linq.Enumerable.FirstOrDefault(assembly.GetManifestResourceNames(), n => n.EndsWith(resourceName));

            if (string.IsNullOrEmpty(fullResourceName))
            {
                Log.LogError($"Could not find embedded resource ending with: {resourceName}");
                Log.LogInfo("Available resources:");
                foreach (var name in assembly.GetManifestResourceNames())
                {
                    Log.LogInfo($"- {name}");
                }
                return null;
            }

            using (Stream stream = assembly.GetManifestResourceStream(fullResourceName))
            {
                if (stream != null)
                {
                    byte[] buffer = new byte[stream.Length];
                    stream.Read(buffer, 0, buffer.Length);
                    Texture2D tex = new Texture2D(2, 2);
                    // LoadImage will auto-resize the texture dimensions.
                    if (tex.LoadImage(buffer))
                    {
                        return tex;
                    }
                }
            }
            return null;
        }

        public class SimpleTooltip : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
        {
            public string tooltipText = "Info";
            private GameObject tooltipInstance;
            private static Font standardFont;

            public void OnPointerEnter(PointerEventData eventData)
            {
                if (tooltipInstance == null)
                {
                    CreateTooltip();
                }
                if (tooltipInstance != null)
                {
                    tooltipInstance.SetActive(true);
                    UpdatePosition(eventData.position);
                }
            }

            public void OnPointerExit(PointerEventData eventData)
            {
                if (tooltipInstance != null) tooltipInstance.SetActive(false);
            }

            private void UpdatePosition(Vector2 position)
            {
                if (tooltipInstance != null)
                {
                    tooltipInstance.transform.position = position + new Vector2(15, -15);
                }
            }

            private void CreateTooltip()
            {
                Canvas canvas = GetComponentInParent<Canvas>();
                if (canvas == null) return;

                tooltipInstance = new GameObject("CustomTooltip");
                tooltipInstance.transform.SetParent(canvas.transform, false);
                tooltipInstance.transform.SetAsLastSibling();

                if (standardFont == null)
                {
                    Text[] allTexts = Resources.FindObjectsOfTypeAll<Text>();
                    if (allTexts != null && allTexts.Length > 0)
                    {
                        foreach (var t in allTexts)
                        {
                            if (t.font != null)
                            {
                                standardFont = t.font;
                                break;
                            }
                        }
                    }
                    if (standardFont == null) standardFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                }

                Image bg = tooltipInstance.AddComponent<Image>();
                bg.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);
                RectTransform rect = tooltipInstance.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(200, 40);
                rect.pivot = new Vector2(0, 1);

                GameObject textObj = new GameObject("TooltipText");
                textObj.transform.SetParent(tooltipInstance.transform, false);
                Text txt = textObj.AddComponent<Text>();
                txt.text = tooltipText;
                txt.font = standardFont;
                txt.alignment = TextAnchor.MiddleCenter;
                txt.color = Color.white;
                txt.resizeTextForBestFit = true;
                txt.resizeTextMinSize = 10;
                txt.resizeTextMaxSize = 20;

                RectTransform textRect = textObj.GetComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(5, 5);
                textRect.offsetMax = new Vector2(-5, -5);
            }

            private void OnDisable()
            {
                if (tooltipInstance != null) Destroy(tooltipInstance);
            }
        }

        public static Texture2D LoadTexture(string path)
        {
            if (File.Exists(path))
            {
                byte[] fileData = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2);
                if (tex.LoadImage(fileData))
                {
                    return tex;
                }
            }
            return null;
        }
    }
}

