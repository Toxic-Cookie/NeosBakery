using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using NeosModLoader;
using HarmonyLib;
using BaseX;
using FrooxEngine;
using FrooxEngine.UIX;

namespace NeosBakery.Core
{
    class Bakery : NeosMod
    {
        public override string Name => "NeosBakery";
        public override string Author => "Toxic_Cookie";
        public override string Version => "1.0.2";
        public override string Link => "https://github.com/Toxic-Cookie/NeosBakery";

        public override void OnEngineInit()
        {
            Harmony harmony = new Harmony("net.Toxic_Cookie.NeosBakery");
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(NeosSwapCanvasPanel), "OnAttach")]
        class DevCreateNewTesting
        {
            public static void Postfix(NeosSwapCanvasPanel __instance)
            {
                DevCreateNewForm createForm = __instance.Slot.GetComponent<DevCreateNewForm>();
                if (createForm == null)
                {
                    return;
                }
                createForm.Slot.GetComponentInChildren<Canvas>().Size.Value = new float2(200f, 700f);
                SyncRef<RectTransform> rectTransform = (SyncRef<RectTransform>)AccessTools.Field(typeof(NeosSwapCanvasPanel), "_currentPanel").GetValue(__instance);
                rectTransform.OnTargetChange += RectTransform_OnTargetChange;
            }
            static void RectTransform_OnTargetChange(SyncRef<RectTransform> reference)
            {
                Engine.Current.WorldManager.FocusedWorld.Coroutines.StartTask(async () =>
                {
                    await Task.Delay(TimeSpan.FromSeconds(Engine.Current.WorldManager.FocusedWorld.Time.Delta + 0.01f)).ConfigureAwait(continueOnCapturedContext: false);
                    await default(ToWorld);

                    List<Text> texts = reference.Target.Slot.GetComponentsInChildren<Text>();

                    if (texts[0] == null)
                    {
                        return;
                    }
                    if (!texts[0].Content.Value.Contains("3D"))
                    {
                        return;
                    }

                    Slot buttonSlot = texts[8].Slot.Parent.Duplicate();
                    buttonSlot.GetComponentInChildren<Text>().Content.Value = "Light Baker Wizard";
                    buttonSlot.GetComponent<ButtonRelay<string>>().Destroy();

                    Button button = buttonSlot.GetComponent<Button>();
                    button.LocalPressed += Button_LocalPressed;
                });
            }
            static void Button_LocalPressed(IButton button, ButtonEventData eventData)
            {
                LightBakerWizard.GetOrCreateWizard();
                button.Slot.GetObjectRoot().Destroy();
            }
        }
    }
}
