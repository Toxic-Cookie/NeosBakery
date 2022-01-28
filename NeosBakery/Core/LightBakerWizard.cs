using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

using HarmonyLib;
using BaseX;
using CodeX;
using FrooxEngine;
using FrooxEngine.UIX;
using Newtonsoft.Json;

using static NeosBakery.Core.Paths;
using static NeosBakery.Core.Defs;

namespace NeosBakery.Core
{
    class LightBakerWizard
    {
        public static LightBakerWizard GetOrCreateWizard()
        {
            if (_Wizard != null)
            {
                WizardSlot.PositionInFrontOfUser(float3.Backward, distance: 1f);
                return _Wizard;
            }
            else
            {
                return new LightBakerWizard();
            }
        }
        static LightBakerWizard _Wizard;
        static Slot WizardSlot;

        CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        readonly ReferenceField<Slot> meshRoot;
        readonly ReferenceField<Slot> lightRoot;
        readonly ValueField<bool> upscale;
        readonly ValueField<int> defaultResolution;
        readonly ValueField<LightType> _bakeType;
        readonly ValueMultiplexer<string> _bakeTypeMultiplexer;
        readonly ValueField<ReflectionProbe.Clear> _bakeMethod;
        readonly ValueMultiplexer<string> _bakeMethodMultiplexer;
        readonly TextField blenderPathField;

        readonly Button bakeButton;
        readonly Button cancelButton;

        readonly Button viewRealtimeButton;
        readonly Button viewBakedButton;

        readonly Button discardBakeButton;
        readonly Button keepBakeButton;
        readonly Button rebakeButton;

        readonly Button saveSettingsButton;
        readonly Button clearCacheButton;

        readonly Text statusText;
        void UpdateStatusText(string info)
        {
            statusText.Content.Value = info;
        }

        protected LightBakerWizard()
        {
            _Wizard = this;

            WizardSlot = Engine.Current.WorldManager.FocusedWorld.RootSlot.AddSlot("Light Baker Wizard");

            BakeSettings bakeSettings = default;
            if (File.Exists(BakeSettingsPath))
            {
                bakeSettings = JsonConvert.DeserializeObject<BakeSettings>(File.ReadAllText(BakeSettingsPath));
            }
            else
            {
                bakeSettings = new BakeSettings(true, 1024, BakeType.DirectAndIndirect, BakeMethod.BurnAlbedo);
                File.WriteAllText(BakeSettingsPath, JsonConvert.SerializeObject(bakeSettings, Formatting.Indented));
            }

            WizardSlot.OnPrepareDestroy += Slot_OnPrepareDestroy;
            WizardSlot.PersistentSelf = false;

            NeosCanvasPanel canvasPanel = WizardSlot.AttachComponent<NeosCanvasPanel>();
            canvasPanel.Panel.AddCloseButton();
            canvasPanel.Panel.AddParentButton();
            canvasPanel.Panel.Title = "Light Baker Wizard";
            canvasPanel.Canvas.Size.Value = new float2(500f, 550f);

            Slot Data = WizardSlot.AddSlot("Data");
            meshRoot = Data.AddSlot("meshRoot").AttachComponent<ReferenceField<Slot>>();
            lightRoot = Data.AddSlot("lightRoot").AttachComponent<ReferenceField<Slot>>();
            upscale = Data.AddSlot("upscale").AttachComponent<ValueField<bool>>();
            upscale.Value.Value = bakeSettings.Upscale;
            defaultResolution = Data.AddSlot("defaultResolution").AttachComponent<ValueField<int>>();
            defaultResolution.Value.Value = bakeSettings.DefaultResolution;
            _bakeType = Data.AddSlot("bakeType").AttachComponent<ValueField<LightType>>();
            _bakeMethod = Data.AddSlot("bakeMethod").AttachComponent<ValueField<ReflectionProbe.Clear>>();

            UIBuilder UI = new UIBuilder(canvasPanel.Canvas);
            UI.Canvas.MarkDeveloper();
            UI.Canvas.AcceptPhysicalTouch.Value = false;
            VerticalLayout verticalLayout = UI.VerticalLayout(4f, childAlignment: Alignment.TopCenter);
            verticalLayout.ForceExpandHeight.Value = false;
            UI.Style.MinHeight = 24f; ;
            UI.Style.PreferredHeight = 24f;

            UI.Text("<b>Developed by Toxic_Cookie with love! <3</b>");
            UI.Text("Mesh Root:").HorizontalAlign.Value = TextHorizontalAlignment.Left;
            UI.Next("Root");
            UI.Current.AttachComponent<RefEditor>().Setup(meshRoot.Reference);
            UI.Text("Light Root:").HorizontalAlign.Value = TextHorizontalAlignment.Left;
            UI.Next("Root");
            UI.Current.AttachComponent<RefEditor>().Setup(lightRoot.Reference);
            UI.HorizontalElementWithLabel("Upscale:", 0.942f, () => UI.BooleanMemberEditor(upscale.Value));
            UI.HorizontalElementWithLabel("Default Resolution:", 0.8f, () => UI.PrimitiveMemberEditor(defaultResolution.Value));

            EnumMemberEditor bakeTypeEditor = UI.HorizontalElementWithLabel("Bake Type:", 0.54f, () => UI.EnumMemberEditor(_bakeType.Value));
            FieldDrive<string> bakeTypetextDrive = (FieldDrive<string>)AccessTools.Field(typeof(EnumMemberEditor), "_textDrive").GetValue(bakeTypeEditor);
            IField<string> bakeTypestringField = bakeTypetextDrive.Target;
            bakeTypetextDrive.Target = null;
            _bakeTypeMultiplexer = _bakeType.Slot.AttachComponent<ValueMultiplexer<string>>();
            _bakeTypeMultiplexer.Values.Add("Direct & Indirect");
            _bakeTypeMultiplexer.Values.Add("Direct");
            _bakeTypeMultiplexer.Values.Add("Indirect");
            _bakeTypeMultiplexer.Target.Target = bakeTypestringField;
            _bakeType.Value.OnValueChange += _bakeType_OnValueChange;
            _bakeType.Value.Value = (LightType)(int)bakeSettings.BakeType;

            EnumMemberEditor bakeMethodEditor = UI.HorizontalElementWithLabel("Bake Method:", 0.54f, () => UI.EnumMemberEditor(_bakeMethod.Value));
            FieldDrive<string> bakeMethodtextDrive = (FieldDrive<string>)AccessTools.Field(typeof(EnumMemberEditor), "_textDrive").GetValue(bakeMethodEditor);
            IField<string> bakeMethodstringField = bakeMethodtextDrive.Target;
            bakeMethodtextDrive.Target = null;
            _bakeMethodMultiplexer = _bakeMethod.Slot.AttachComponent<ValueMultiplexer<string>>();
            _bakeMethodMultiplexer.Values.Add("Separate Albedo");
            _bakeMethodMultiplexer.Values.Add("Burn Albedo");
            _bakeMethodMultiplexer.Target.Target = bakeMethodstringField;
            _bakeMethod.Value.OnValueChange += _bakeMethod_OnValueChange;
            _bakeMethod.Value.Value = (ReflectionProbe.Clear)(int)bakeSettings.BakeMethod;

            UI.Text("Blender Path:").HorizontalAlign.Value = TextHorizontalAlignment.Left;
            blenderPathField = UI.TextField(BlenderPath);

            UI.HorizontalLayout(4f);
            bakeButton = UI.Button("Bake");
            bakeButton.LocalPressed += Bake;

            cancelButton = UI.Button("Cancel");
            cancelButton.LocalPressed += Cancel;
            cancelButton.EnabledField.Value = false;
            LightBaker.OnBakeCancelled += OnCancelled;
            UI.NestOut();

            UI.HorizontalLayout(4f);
            viewRealtimeButton = UI.Button("View Realtime");
            viewRealtimeButton.LocalPressed += ViewRealtime;
            viewRealtimeButton.EnabledField.Value = false;

            viewBakedButton = UI.Button("View Baked");
            viewBakedButton.LocalPressed += ViewBaked;
            viewBakedButton.EnabledField.Value = false;
            UI.NestOut();

            UI.HorizontalLayout(4f);
            discardBakeButton = UI.Button("Discard Bake");
            discardBakeButton.LocalPressed += DiscardBake;
            discardBakeButton.EnabledField.Value = false;

            keepBakeButton = UI.Button("Keep Bake");
            keepBakeButton.LocalPressed += KeepBake;
            keepBakeButton.EnabledField.Value = false;

            rebakeButton = UI.Button("Rebake");
            rebakeButton.LocalPressed += Rebake;
            rebakeButton.EnabledField.Value = false;
            UI.NestOut();

            saveSettingsButton = UI.Button("Save Settings");
            saveSettingsButton.LocalPressed += SaveSettings;

            clearCacheButton = UI.Button("Clear Cache");
            clearCacheButton.LocalPressed += ClearCache;

            LightBaker.OnBakeInfo += UpdateStatusText;

            UI.Text("Status:");
            statusText = UI.Text("Idle...");

            WizardSlot.PositionInFrontOfUser(float3.Backward, distance: 1f);
        }

        void _bakeType_OnValueChange(SyncField<LightType> syncField)
        {
            switch (syncField.Value)
            {
                case LightType.Point:
                    _bakeTypeMultiplexer.Index.Value = 0;
                    break;
                case LightType.Directional:
                    _bakeTypeMultiplexer.Index.Value = 1;
                    break;
                case LightType.Spot:
                    _bakeTypeMultiplexer.Index.Value = 2;
                    break;
            }
        }
        void _bakeMethod_OnValueChange(SyncField<ReflectionProbe.Clear> syncField)
        {
            switch (syncField.Value)
            {
                case ReflectionProbe.Clear.Skybox:
                    _bakeMethodMultiplexer.Index.Value = 0;
                    break;
                case ReflectionProbe.Clear.Color:
                    _bakeMethodMultiplexer.Index.Value = 1;
                    break;
            }
        }

        void Slot_OnPrepareDestroy(Slot slot)
        {
            LightBaker.OnBakeCancelled -= OnCancelled;
            LightBaker.OnBakeInfo -= UpdateStatusText;

            if (LightBaker.IsBusy && !LightBaker.IsFinalized)
            {
                Cancel(null, default);
            }
            else if (!LightBaker.IsBusy && !LightBaker.IsFinalized)
            {
                KeepBake(null, default);
            }

            _Wizard = null;
        }

        void Bake(IButton button, ButtonEventData eventData)
        {
            Engine.Current.WorldManager.FocusedWorld.Coroutines.StartTask(async () =>
            {
                UpdateAllButtonStates(PressedButton.Bake, StateType.Initial);
                bool result = await LightBaker.Bake(meshRoot.Reference.Target, lightRoot.Reference.Target, blenderPathField.Text.Content.Value, defaultResolution.Value, upscale.Value, (BakeType)(int)_bakeType.Value.Value, (BakeMethod)(int)_bakeMethod.Value.Value, cancellationTokenSource.Token);
                if (result)
                {
                    UpdateAllButtonStates(PressedButton.Bake, StateType.Completed);
                }
                else
                {
                    cancelButton.EnabledField.Value = false;
                    bakeButton.EnabledField.Value = true;
                    clearCacheButton.EnabledField.Value = true;
                }
            });

        }
        void Cancel(IButton button, ButtonEventData eventData)
        {
            UpdateAllButtonStates(PressedButton.Cancel, StateType.Initial);
            UpdateStatusText("Cancelling bake job...");
            cancellationTokenSource.Cancel();
        }
        void OnCancelled()
        {
            UpdateAllButtonStates(PressedButton.Cancel, StateType.Completed);
            cancellationTokenSource = new CancellationTokenSource();
        }

        void ViewRealtime(IButton button, ButtonEventData eventData)
        {
            LightBaker.ViewChanges(ViewType.Realtime);
        }
        void ViewBaked(IButton button, ButtonEventData eventData)
        {
            LightBaker.ViewChanges(ViewType.Baked);
        }

        void DiscardBake(IButton button, ButtonEventData eventData)
        {
            LightBaker.FinalizeChanges(ChangesType.Discard);
            UpdateAllButtonStates(PressedButton.DiscardBake, StateType.Completed);
        }
        void KeepBake(IButton button, ButtonEventData eventData)
        {
            LightBaker.FinalizeChanges(ChangesType.Keep);
            UpdateAllButtonStates(PressedButton.KeepBake, StateType.Completed);
        }
        void Rebake(IButton button, ButtonEventData eventData)
        {
            DiscardBake(null, default);
            Bake(null, default);
        }

        void SaveSettings(IButton button, ButtonEventData eventData)
        {
            File.WriteAllText(BakeSettingsPath, JsonConvert.SerializeObject(new BakeSettings(upscale.Value, defaultResolution.Value, (BakeType)(int)_bakeType.Value.Value, (BakeMethod)(int)_bakeMethod.Value.Value), Formatting.Indented));
            SetBlenderPath(blenderPathField.Text.Content.Value);
            UpdateStatusText("Settings succesfully saved!");
        }
        void ClearCache(IButton button, ButtonEventData eventData)
        {
            LightBaker.ClearCache();
        }

        void UpdateAllButtonStates(PressedButton pressedButton, StateType stateType)
        {
            switch (stateType)
            {
                case StateType.Initial:
                    switch (pressedButton)
                    {
                        case PressedButton.Bake:
                            cancelButton.EnabledField.Value = true;
                            bakeButton.EnabledField.Value = false;
                            clearCacheButton.EnabledField.Value = false;
                            break;
                        case PressedButton.Cancel:
                            cancelButton.EnabledField.Value = false;
                            break;
                        default:
                            break;
                    }
                    break;
                case StateType.Completed:
                    switch (pressedButton)
                    {
                        case PressedButton.Bake:
                            viewRealtimeButton.EnabledField.Value = true;
                            viewBakedButton.EnabledField.Value = true;
                            discardBakeButton.EnabledField.Value = true;
                            keepBakeButton.EnabledField.Value = true;
                            rebakeButton.EnabledField.Value = true;
                            cancelButton.EnabledField.Value = false;
                            break;
                        case PressedButton.Cancel:
                            bakeButton.EnabledField.Value = true;
                            clearCacheButton.EnabledField.Value = true;
                            break;
                        case PressedButton.DiscardBake:
                            bakeButton.EnabledField.Value = true;
                            clearCacheButton.EnabledField.Value = true;
                            viewRealtimeButton.EnabledField.Value = false;
                            viewBakedButton.EnabledField.Value = false;
                            discardBakeButton.EnabledField.Value = false;
                            keepBakeButton.EnabledField.Value = false;
                            rebakeButton.EnabledField.Value = false;
                            break;
                        case PressedButton.KeepBake:
                            bakeButton.EnabledField.Value = true;
                            clearCacheButton.EnabledField.Value = true;
                            viewRealtimeButton.EnabledField.Value = false;
                            viewBakedButton.EnabledField.Value = false;
                            discardBakeButton.EnabledField.Value = false;
                            keepBakeButton.EnabledField.Value = false;
                            rebakeButton.EnabledField.Value = false;
                            break;
                    }
                    break;
            }
        }
        enum PressedButton
        {
            Bake,
            Cancel,
            DiscardBake,
            KeepBake
        }
        enum StateType
        {
            Initial,
            Completed
        }

        struct BakeSettings
        {
            public readonly bool Upscale;
            public readonly int DefaultResolution;
            public readonly BakeType BakeType;
            public readonly BakeMethod BakeMethod;

            public BakeSettings(bool upscale, int defaultResolution, BakeType bakeType, BakeMethod bakeMethod)
            {
                Upscale = upscale;
                DefaultResolution = defaultResolution;
                BakeType = bakeType;
                BakeMethod = bakeMethod;
            }
        }
    }
}
