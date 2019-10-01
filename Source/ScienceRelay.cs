#region license

/*The MIT License (MIT)

ScienceRelay - MonoBehaviour for controlling the transfer of science from one vessel to another

Copyright (c) 2016 DMagic

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.
*/

#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CommNet;
using FinePrint.Utilities;
using JetBrains.Annotations;
using KSP.Localization;
using KSP.UI.Screens.Flight.Dialogs;
using KSP.UI.TooltipTypes;
using UnityEngine;
using UnityEngine.UI;

namespace ScienceRelay.Source
{
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class ScienceRelay : MonoBehaviour
	{
		private static Sprite _transferNormal;
		private static Sprite _transferHighlight;
		private static Sprite _transferActive;
		private static bool _spritesLoaded;
		private static MethodInfo _occlusionMethod;
		private static bool _reflected;
		private static bool _cnConstellationChecked;
		private static bool _cnConstellationLoaded;
		private List<KeyValuePair<Vessel, double>> _connectedVessels = new List<KeyValuePair<Vessel, double>>();
		private ExperimentResultDialogPage _currentPage;
		private readonly CommPath _pathCache = new CommPath();
		private readonly List<ScienceRelayData> _queuedData = new List<ScienceRelayData>();
		private ExperimentsResultDialog _resultsDialog;
		private ScienceRelayParameters _settings;
		private bool _transferAll;
		private Button _transferButton;
		private PopupDialog _transferDialog;

		private string _version;
		private PopupDialog _warningDialog;

		public static ScienceRelay Instance { get; private set; }

		[UsedImplicitly]
		private void Awake()
		{
			if (HighLogic.CurrentGame.Mode == Game.Modes.SANDBOX) {
				Destroy(gameObject);
				return;
			}

			if (Instance != null) {
				Destroy(gameObject);
				return;
			}

			if (!_reflected) {
				assignReflection();
			}

			if (!_cnConstellationChecked) {
				CommNetConstellationCheck();
			}

			if (!_spritesLoaded) {
				LoadSprite();
			}

			Instance = this;

			ProcessPrefab();
		}

		[UsedImplicitly]
		private void Start()
		{
			ScienceRelayDialog.onDialogSpawn.Add(OnSpawn);
			ScienceRelayDialog.onDialogClose.Add(OnClose);
			GameEvents.OnTriggeredDataTransmission.Add(onTriggeredData);
			GameEvents.onGamePause.Add(OnPause);
			GameEvents.onGameUnpause.Add(OnUnpause);
			GameEvents.OnGameSettingsApplied.Add(OnSettingsApplied);

			_settings = HighLogic.CurrentGame.Parameters.CustomParams<ScienceRelayParameters>();

			if (_settings == null) {
				Instance = null;
				Destroy(gameObject);
			}

			var assembly = AssemblyLoader.loadedAssemblies.GetByAssembly(Assembly.GetExecutingAssembly()).assembly;
			var assembly_informational_version_attribute = Attribute.GetCustomAttribute(assembly, typeof(AssemblyInformationalVersionAttribute)) as AssemblyInformationalVersionAttribute;
			_version = assembly_informational_version_attribute != null ? assembly_informational_version_attribute.InformationalVersion : "";
		}

		[UsedImplicitly]
		private void OnDestroy()
		{
			Instance = null;

			popupDismiss();

			ScienceRelayDialog.onDialogSpawn.Remove(OnSpawn);
			ScienceRelayDialog.onDialogClose.Remove(OnClose);
			GameEvents.OnTriggeredDataTransmission.Remove(onTriggeredData);
			GameEvents.onGamePause.Remove(OnPause);
			GameEvents.onGameUnpause.Remove(OnUnpause);
			GameEvents.OnGameSettingsApplied.Remove(OnSettingsApplied);
		}

		private void OnSettingsApplied()
		{
			_settings = HighLogic.CurrentGame.Parameters.CustomParams<ScienceRelayParameters>();
		}

		private void LoadSprite()
		{
			var normal = GameDatabase.Instance.GetTexture("ScienceRelay/Resources/Relay_Normal", false);
			var highlight = GameDatabase.Instance.GetTexture("ScienceRelay/Resources/Relay_Highlight", false);
			var active = GameDatabase.Instance.GetTexture("ScienceRelay/Resources/Relay_Active", false);

			if (normal == null || highlight == null || active == null) {
				return;
			}

			_transferNormal = Sprite.Create(normal, new Rect(0, 0, normal.width, normal.height), new Vector2(0.5f, 0.5f));
			_transferHighlight = Sprite.Create(highlight, new Rect(0, 0, highlight.width, highlight.height), new Vector2(0.5f, 0.5f));
			_transferActive = Sprite.Create(active, new Rect(0, 0, active.width, active.height), new Vector2(0.5f, 0.5f));

			_spritesLoaded = true;
		}

		private void OnPause()
		{
			if (_transferDialog != null) {
				_transferDialog.gameObject.SetActive(false);
			}

			if (_warningDialog != null) {
				_warningDialog.gameObject.SetActive(false);
			}
		}

		private void OnUnpause()
		{
			if (_transferDialog != null) {
				_transferDialog.gameObject.SetActive(true);
			}

			if (_warningDialog != null) {
				_warningDialog.gameObject.SetActive(true);
			}
		}

		private void ProcessPrefab()
		{
			var prefab = AssetBase.GetPrefab("ScienceResultsDialog");

			if (prefab == null) {
				return;
			}

			var dialog_listener = prefab.gameObject.AddOrGetComponent<ScienceRelayDialog>();

			var buttons = prefab.GetComponentsInChildren<Button>(true);

			for (var i = buttons.Length - 1; i >= 0; i--) {
				var b = buttons[i];

				if (b.name == "ButtonPrev") {
					dialog_listener.buttonPrev = b;
				} else if (b.name == "ButtonNext") {
					dialog_listener.buttonNext = b;
				} else if (b.name == "ButtonKeep") {
					_transferButton = Instantiate(b, b.transform.parent);

					_transferButton.name = "ButtonTransfer";

					_transferButton.onClick.RemoveAllListeners();

					var tooltip = _transferButton.GetComponent<TooltipController_Text>();

					if (tooltip != null) {
						tooltip.textString = Localizer.Format("#autoLOC_ScienceRelay_Tooltip");
					}

					if (_spritesLoaded) {
						var select = _transferButton.GetComponent<Selectable>();

						if (select != null) {
							select.image.sprite = _transferNormal;
							select.image.type = Image.Type.Simple;
							select.transition = Selectable.Transition.SpriteSwap;

							var state = select.spriteState;
							state.highlightedSprite = _transferHighlight;
							state.pressedSprite = _transferActive;
							state.disabledSprite = _transferActive;
							select.spriteState = state;
						}
					}

					dialog_listener.buttonTransfer = _transferButton;
				}
			}

			RelayLog("Science results prefab processed...");
		}

		private void OnSpawn(ExperimentsResultDialog dialog)
		{
			if (dialog == null) {
				return;
			}

			_resultsDialog = dialog;

			var buttons = _resultsDialog.GetComponentsInChildren<Button>(true);

			for (var i = buttons.Length - 1; i >= 0; i--) {
				var b = buttons[i];

				if (b == null) {
					continue;
				}

				if (b.name != "ButtonTransfer") {
					continue;
				}

				_transferButton = b;
				break;
			}

			_currentPage = _resultsDialog.currentPage;

			if (_currentPage.pageData != null) {
				_currentPage.pageData.baseTransmitValue = _currentPage.xmitDataScalar;
			}

			_transferButton.gameObject.SetActive(getConnectedVessels());
		}

		private void OnClose(ExperimentsResultDialog dialog)
		{
			if (dialog == null || _resultsDialog == null) {
				return;
			}

			if (dialog == _resultsDialog) {
				_resultsDialog = null;
				_transferButton = null;
				_currentPage = null;
			}

			popupDismiss();
		}

		public void OnPageChange()
		{
			if (_resultsDialog == null) {
				return;
			}

			_currentPage = _resultsDialog.currentPage;

			if (_currentPage.pageData != null) {
				_currentPage.pageData.baseTransmitValue = _currentPage.xmitDataScalar;
			}

			popupDismiss();
		}

		public void onTransfer()
		{
			if (_resultsDialog == null) {
				return;
			}

			if (_currentPage == null) {
				return;
			}

			if (_currentPage.pageData == null) {
				return;
			}

			if (_connectedVessels.Count <= 0) {
				return;
			}

			_transferAll = false;

			_transferDialog = spawnDialog(_currentPage);
		}

		private void popupDismiss()
		{
			if (_transferDialog != null) {
				_transferDialog.Dismiss();
			}
		}

		private PopupDialog spawnDialog(ExperimentResultDialogPage page)
		{
			var dialog = new List<DialogGUIBase>();

			dialog.Add(new DialogGUILabel(Localizer.Format("#autoLOC_ScienceRelay_Transmit", page.pageData.title)));

			_transferAll = false;

			if (_resultsDialog.pages.Count > 1) {
				dialog.Add(new DialogGUIToggle(false,
					Localizer.Format("#autoLOC_ScienceRelay_TransmitAll"),
					delegate {
						_transferAll = !_transferAll;
					})
				);
			}

			var vessels = new List<DialogGUIHorizontalLayout>();

			for (var i = _connectedVessels.Count - 1; i >= 0; i--) {
				var pair = _connectedVessels[i];

				var v = pair.Key;

				var boost = signalBoost((float) pair.Value, v, page.pageData, page.xmitDataScalar);

				DialogGUILabel label = null;

				if (_settings.transmissionBoost) {
					var transmit = string.Format("Xmit: {0:P0}", page.xmitDataScalar * (1 + boost));

					if (boost > 0) {
						transmit += string.Format("(+{0:P0})", boost);
					}

					label = new DialogGUILabel(transmit, 130, 25);
				}

				DialogGUIBase button = null;

				if (_settings.showTransmitWarning && page.showTransmitWarning) {
					button = new DialogGUIButton(
						v.vesselName,
						delegate {
							spawnWarningDialog(
								new ScienceRelayData
								{
									data = page.pageData,
									host = page.host,
									boost = boost,
									source = FlightGlobals.ActiveVessel,
									target = v
								},
								page.transmitWarningMessage);
						},
						160,
						30,
						true, button);
				} else {
					button = new DialogGUIButton<ScienceRelayData>(
						v.vesselName,
						transferToVessel,
						new ScienceRelayData
						{
							data = page.pageData,
							host = page.host,
							boost = boost,
							source = FlightGlobals.ActiveVessel,
							target = v
						},
						true);

					button.size = new Vector2(160, 30);
				}

				var h = new DialogGUIHorizontalLayout(true, false, 4, new RectOffset(), TextAnchor.MiddleCenter, button);

				if (label != null) {
					h.AddChild(label);
				}

				vessels.Add(h);
			}

			var scrollList = new DialogGUIBase[vessels.Count + 1];

			scrollList[0] = new DialogGUIContentSizer(ContentSizeFitter.FitMode.Unconstrained, ContentSizeFitter.FitMode.PreferredSize, true);

			for (var i = 0; i < vessels.Count; i++) {
				scrollList[i + 1] = vessels[i];
			}

			dialog.Add(new DialogGUIScrollList(new Vector2(270, 200), false, true,
				new DialogGUIVerticalLayout(10, 100, 4, new RectOffset(6, 24, 10, 10), TextAnchor.MiddleLeft, scrollList)
			));

			dialog.Add(new DialogGUISpace(4));

			dialog.Add(new DialogGUIHorizontalLayout(new DialogGUIFlexibleSpace(), new DialogGUIButton(Localizer.Format("#autoLOC_190768"), popupDismiss), new DialogGUIFlexibleSpace(), new DialogGUILabel(_version)));

			var resultRect = _resultsDialog.GetComponent<RectTransform>();

			var pos = new Rect(0.5f, 0.5f, 300, 300);

			if (resultRect != null) {
				Vector2 resultPos = resultRect.position;

				var scale = GameSettings.UI_SCALE;

				var width = Screen.width;
				var height = Screen.height;

				var xpos = resultPos.x / scale + width / 2;
				var ypos = resultPos.y / scale + height / 2;

				var yNorm = ypos / height;

				pos.y = yNorm;

				pos.x = xpos > width - 550 * scale ? (xpos - 360) / width : (xpos + 360) / width;
			}

			return PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new MultiOptionDialog(
				"ScienceRelayDialog",
				"",
				"Science Relay",
				UISkinManager.defaultSkin,
				pos,
				dialog.ToArray()), false, UISkinManager.defaultSkin);
		}

		private void spawnWarningDialog(ScienceRelayData data, string message)
		{
			_warningDialog = PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new MultiOptionDialog(
					"ScienceRelayWarning",
					Localizer.Format("#autoLOC_6001489", message),
					Localizer.Format("#autoLOC_236416"),
					UISkinManager.defaultSkin,
					new Rect(0.5f, 0.5f, 250, 120), new DialogGUIButton<ScienceRelayData>(
						Localizer.Format("#autoLOC_6001430"),
						transferToVessel,
						data,
						true
					), new DialogGUIButton(Localizer.Format("#autoLOC_190768"), null, true)),
				false, UISkinManager.defaultSkin);
		}

		private float signalBoost(float s, Vessel target, ScienceData data, float xmit)
		{
			float f = 0;

			if (target == null) {
				return f;
			}

			if (_settings.requireMPLForBoost && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", target)) {
				return f;
			}

			if (s <= 0) {
				return f;
			}

			if (data == null) {
				return f;
			}

			var sub = ResearchAndDevelopment.GetSubjectByID(data.subjectID);

			if (sub == null) {
				return f;
			}

			var recoveredData = ResearchAndDevelopment.GetScienceValue(data.dataAmount, sub);
			var transmitData = ResearchAndDevelopment.GetScienceValue(data.dataAmount, sub, xmit);

			if (recoveredData <= 0) {
				return f;
			}

			if (transmitData <= 0) {
				return f;
			}

			if (transmitData * s > recoveredData) {
				f = recoveredData / transmitData;
			} else {
				f = s;
			}

			f -= 1;

			f = (1 - _settings.transmissionPenalty) * f;

			return f;
		}

		private void transferToVessel(ScienceRelayData RelayData)
		{
			//if (resultsDialog != null) resultsDialog.Dismiss();  
			// JLB -- Leaves dialog up for other experiments
			if (_resultsDialog != null && _transferAll) _resultsDialog.Dismiss();
			if (_resultsDialog != null && !_transferAll) _resultsDialog.currentPage.OnKeepData(_resultsDialog.currentPage.pageData);

			if (RelayData.host == null || RelayData.data == null || RelayData.target == null || RelayData.source == null) {
				return;
			}

			var data = new List<ScienceRelayData>();

			if (_transferAll) {
				for (var i = _resultsDialog.pages.Count - 1; i >= 0; i--) {
					var page = _resultsDialog.pages[i];

					if (page == null) {
						continue;
					}

					if (page.pageData == null) {
						continue;
					}

					if (page.host == null) {
						continue;
					}

					var relayData = new ScienceRelayData
					{
						data = page.pageData,
						host = page.host,
						boost = signalBoost(RelayData.boost + 1, RelayData.target, page.pageData, page.xmitDataScalar),
						target = RelayData.target,
						source = RelayData.source
					};

					relayData.data.baseTransmitValue = page.xmitDataScalar;

					data.Add(relayData);
				}
			} else {
				RelayData.data.baseTransmitValue = _currentPage.xmitDataScalar;
				data.Add(RelayData);
			}

			for (var i = data.Count - 1; i >= 0; i--) {
				var d = data[i].data;

				var host = data[i].host;

				var containers = host.FindModulesImplementing<IScienceDataContainer>();

				IScienceDataContainer hostContainer = null;

				for (var j = containers.Count - 1; j >= 0; j--) {
					hostContainer = null;

					var container = containers[j];

					if (container == null) {
						continue;
					}

					var containerData = container.GetData();

					for (var k = containerData.Length - 1; k >= 0; k--) {
						var dat = containerData[k];

						if (dat.subjectID == d.subjectID) {
							hostContainer = container;
							break;
						}
					}

					if (hostContainer != null) {
						break;
					}
				}

				var bestTransmitter = ScienceUtil.GetBestTransmitter(RelayData.source);
				//IScienceDataTransmitter bestTransmitter = ScienceUtil.GetBestTransmitter(RelayData._source.FindPartModulesImplementing<IScienceDataTransmitter>());

				if (bestTransmitter == null) {
					if (CommNetScenario.CommNetEnabled) {
						ScreenMessages.PostScreenMessage(Localizer.Format("#autoLOC_238505"), 3, ScreenMessageStyle.UPPER_CENTER);
					} else {
						ScreenMessages.PostScreenMessage(Localizer.Format("#autoLOC_238507"), 3, ScreenMessageStyle.UPPER_CENTER);
					}
				} else {
					RelayLog("Attempting to transmit science data: [{0}] to vessel: [{1}] at boost level: {2:P2}", data[i].data.title, data[i].target.vesselName, data[i].boost);

					d.triggered = true;

					bestTransmitter.TransmitData(new List<ScienceData> {d});

					_queuedData.Add(data[i]);

					if (hostContainer != null) {
						hostContainer.DumpData(d);
					}
				}
			}
		}

		private void onTriggeredData(ScienceData data, Vessel vessel, bool aborted)
		{
			if (vessel == null) {
				return;
			}

			if (vessel != FlightGlobals.ActiveVessel) {
				return;
			}

			if (data == null) {
				return;
			}

			for (var i = _queuedData.Count - 1; i >= 0; i--) {
				var d = _queuedData[i];

				if (d.data.subjectID != data.subjectID) {
					continue;
				}

				if (aborted) {
					RelayLog("Science data: [{0}] transmission to vessel: [{1}] aborted, returning to sender: [{2}]", d.data.title, d.target.vesselName, d.source.vesselName);
					data.triggered = false;
					return;
				}

				if (!finishTransfer(d.target, d.data, d.boost)) {
					RelayLog("Data transfer failed; returning to sender: [{0}]", d.source.vesselName);

					var host = d.host;

					var containers = host.FindModulesImplementing<IScienceDataContainer>();

					IScienceDataContainer hostContainer = null;

					for (var j = containers.Count - 1; j >= 0; j--) {
						var container = containers[j];

						if (container == null) {
							continue;
						}

						var mod = container as PartModule;

						if (mod.part == null) {
							continue;
						}

						if (mod.part.flightID != data.container) {
							continue;
						}

						hostContainer = container;
						break;
					}

					if (hostContainer != null) {
						data.triggered = false;
						hostContainer.ReturnData(data);
					}
				} else {
					RelayLog("Science data: [{0}] successfully transmitted to vessel: [{1}] from vessel: [{2}]", d.data.title, d.target.vesselName, d.source.vesselName);
					ScreenMessages.PostScreenMessage(Localizer.Format("#autoLOC_238419", d.target.vesselName, data.dataAmount.ToString("F0"), data.title),
						4, ScreenMessageStyle.UPPER_LEFT);
				}

				_queuedData.Remove(d);

				break;
			}
		}

		private bool finishTransfer(Vessel v, ScienceData d, float boost)
		{
			if (v == null) {
				return false;
			}

			if (d == null) {
				return false;
			}

			if (v.loaded) {
				var containers = v.FindPartModulesImplementing<ModuleScienceContainer>();

				if (containers.Count <= 0) {
					return false;
				}

				ModuleScienceContainer currentContainer = null;

				for (var j = containers.Count - 1; j >= 0; j--) {
					var container = containers[j];

					if (container.capacity != 0 && container.GetData().Length >= container.capacity) {
						continue;
					}

					if (container.allowRepeatedSubjects) {
						currentContainer = container;
						break;
					}

					if (container.HasData(d)) {
						continue;
					}

					currentContainer = container;
				}

				if (currentContainer != null) {
					d.triggered = false;
					d.dataAmount *= d.baseTransmitValue * (1 + boost);
					d.transmitBonus = 1;
					d.baseTransmitValue = 1;
					return currentContainer.AddData(d);
				}

				RelayLog("No valid science container for science: [{0}] found on vessel: [{1}]", d.title, v.vesselName);
			} else {
				var containers = getProtoContainers(v.protoVessel);

				if (containers.Count <= 0) {
					return false;
				}

				ProtoPartModuleSnapshot currentContainer = null;

				uint host = 0;

				for (var j = containers.Count - 1; j >= 0; j--) {
					var container = containers[j];

					host = container.flightID;

					ProtoPartModuleSnapshot tempContainer = null;

					for (var k = container.modules.Count - 1; k >= 0; k--) {
						var mod = container.modules[k];

						if (mod.moduleName != "ModuleScienceContainer") {
							continue;
						}

						tempContainer = mod;

						break;
					}

					if (tempContainer == null) {
						continue;
					}

					var protoData = new List<ScienceData>();

					var science = tempContainer.moduleValues.GetNodes("ScienceData");

					for (var l = science.Length - 1; l >= 0; l--) {
						var node = science[l];

						protoData.Add(new ScienceData(node));
					}

					var prefab = container.partInfo.partPrefab;

					var prefabContainer = prefab.FindModuleImplementing<ModuleScienceContainer>();

					if (prefabContainer != null) {
						if (prefabContainer.capacity != 0 && protoData.Count >= prefabContainer.capacity) {
							continue;
						}

						if (prefabContainer.allowRepeatedSubjects) {
							currentContainer = tempContainer;
							break;
						}

						if (HasData(d.subjectID, protoData)) {
							continue;
						}

						currentContainer = tempContainer;
					}
				}

				if (currentContainer != null) {
					d.triggered = false;
					d.dataAmount = d.dataAmount * (d.baseTransmitValue * (boost + 1));
					d.transmitBonus = 1;
					d.baseTransmitValue = 1;
					d.container = host;
					d.Save(currentContainer.moduleValues.AddNode("ScienceData"));
					return true;
				}

				RelayLog("No valid science container for science: [{0}] found on vessel: [{1}]", d.title, v.vesselName);
			}

			return false;
		}

		private bool HasData(string id, List<ScienceData> data)
		{
			for (var i = data.Count - 1; i >= 0; i--) {
				var d = data[i];

				if (d.subjectID == id) {
					return true;
				}
			}

			return false;
		}

		private bool labWithCrew(Vessel v)
		{
			if (v == null || v.protoVessel == null) {
				return false;
			}

			if (v.loaded) {
				var labs = v.FindPartModulesImplementing<ModuleScienceLab>();

				for (var i = labs.Count - 1; i >= 0; i--) {
					var lab = labs[i];

					if (lab == null) {
						continue;
					}

					if (lab.part.protoModuleCrew.Count >= lab.crewsRequired) {
						return true;
					}
				}
			} else {
				for (var i = v.protoVessel.protoPartSnapshots.Count - 1; i >= 0; i--) {
					var part = v.protoVessel.protoPartSnapshots[i];

					if (part == null) {
						continue;
					}

					for (var j = part.modules.Count - 1; j >= 0; j--) {
						var mod = part.modules[j];

						if (mod == null) {
							continue;
						}

						if (mod.moduleName != "ModuleScienceLab") {
							continue;
						}

						var crew = (int) getCrewRequired(part);

						if (part.protoModuleCrew.Count >= crew) {
							return true;
						}
					}
				}
			}

			return false;
		}

		private float getCrewRequired(ProtoPartSnapshot part)
		{
			if (part == null) {
				return 0;
			}

			var a = PartLoader.getPartInfoByName(part.partName);

			if (a == null) {
				return 0;
			}

			var prefab = a.partPrefab;

			if (prefab == null) {
				return 0;
			}

			for (var i = prefab.Modules.Count - 1; i >= 0; i--) {
				var mod = prefab.Modules[i];

				if (mod == null) {
					continue;
				}

				if (mod.moduleName != "ModuleScienceLab") {
					continue;
				}

				return ((ModuleScienceLab) mod).crewsRequired;
			}

			return 0;
		}

		private List<ProtoPartSnapshot> getProtoContainers(ProtoVessel v)
		{
			var parts = new List<ProtoPartSnapshot>();

			for (var i = v.protoPartSnapshots.Count - 1; i >= 0; i--) {
				var p = v.protoPartSnapshots[i];

				if (p == null) {
					continue;
				}

				var b = false;

				for (var j = p.modules.Count - 1; j >= 0; j--) {
					var mod = p.modules[j];

					if (mod == null) {
						continue;
					}

					if (mod.moduleName == "ModuleScienceContainer") {
						parts.Add(p);
						b = true;
						break;
					}
				}

				if (b) {
					break;
				}
			}

			return parts;
		}

		private bool getConnectedVessels()
		{
			_connectedVessels.Clear();

			if (_resultsDialog == null) {
				return false;
			}

			var vessel = FlightGlobals.ActiveVessel;

			if (vessel == null) {
				return false;
			}

			//RelayLog("Finding Connected Vessels...");

			if (CommNetScenario.CommNetEnabled) // && !CNConstellationLoaded)
			{
				_connectedVessels = getConnectedVessels(vessel);
			} else {
				//RelayLog("No connection status required");

				for (var i = FlightGlobals.Vessels.Count - 1; i >= 0; i--) {
					var v = FlightGlobals.Vessels[i];

					if (v == null) {
						continue;
					}

					if (v == vessel) {
						continue;
					}

					var type = v.vesselType;

					if (type == VesselType.Debris || type == VesselType.SpaceObject || type == VesselType.Unknown || type == VesselType.Flag) {
						continue;
					}

					if (_settings.requireMPL && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", v)) {
						continue;
					}

					if (!VesselUtilities.VesselHasModuleName("ModuleScienceContainer", v)) {
						continue;
					}

					_connectedVessels.Add(new KeyValuePair<Vessel, double>(v, 0));
				}
			}

			RelayLog("Connected vessels detected for science transmission: {0}", _connectedVessels.Count);

			return _connectedVessels.Count > 0;
		}

		private List<KeyValuePair<Vessel, double>> getConnectedVessels(Vessel v)
		{
			var connections = new List<KeyValuePair<Vessel, double>>();

			var checkNodes = new List<KeyValuePair<CommNode, double>>();

			//RelayLog("Parsing vessels for connection");

			if (v.connection != null) {
				var source = v.connection.Comm;

				if (source != null) {
					if (!_settings.requireRelay) {
						checkNodes.Add(new KeyValuePair<CommNode, double>(source, 1));
					}

					//RelayLog("Source node valid");

					var net = v.connection.Comm.Net;

					if (net != null) {
						//RelayLog("Source network valid");

						for (var i = FlightGlobals.Vessels.Count - 1; i >= 0; i--) {
							var otherVessel = FlightGlobals.Vessels[i];

							if (otherVessel == null) {
								continue;
							}

							if (otherVessel == v) {
								continue;
							}

							var type = otherVessel.vesselType;

							if (type == VesselType.Debris || type == VesselType.SpaceObject || type == VesselType.Unknown || type == VesselType.Flag || type == VesselType.EVA) {
								continue;
							}

							if (otherVessel.connection == null || otherVessel.connection.Comm == null) {
								continue;
							}

							//RelayLog("Vessel status check for\n---- {0} ----", otherVessel.vesselName);

							if (otherVessel.connection.ControlPath != null && otherVessel.connection.ControlPath.First != null) {
								if (otherVessel.connection.ControlPath.First.cost < 0.0001) {
									continue;
								}
							}

							if (!net.FindPath(source, _pathCache, otherVessel.connection.Comm)) {
								continue;
							}

							if (_pathCache == null) {
								continue;
							}

							if (_pathCache.Count <= 0) {
								continue;
							}

							//RelayLog("Vessel network path valid");

							if (!_settings.requireRelay) {
								//RelayLog("Searching for direct paths");

								double totalStrength = 1;

								var l = _pathCache.Count;

								for (var j = 0; j < l; j++) {
									var link = _pathCache[j];

									totalStrength *= link.signalStrength;

									//RelayLog("Checking ling status...");

									if (!link.a.isHome && !updateCommNode(checkNodes, link.a, totalStrength)) {
										checkNodes.Add(new KeyValuePair<CommNode, double>(link.a, totalStrength));
									}

									if (!link.b.isHome && !updateCommNode(checkNodes, link.b, totalStrength)) {
										checkNodes.Add(new KeyValuePair<CommNode, double>(link.b, totalStrength));
									}
								}
							}

							if (_settings.requireMPL && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", otherVessel)) {
								continue;
							}

							if (!VesselUtilities.VesselHasModuleName("ModuleScienceContainer", otherVessel)) {
								continue;
							}

							var s = _pathCache.signalStrength;

							s = source.scienceCurve.Evaluate(s);

							//RelayLog("Vessel has valid containers - Connection status: {0:N2}", s);

							connections.Add(new KeyValuePair<Vessel, double>(otherVessel, s + 1));
						}

						if (!_settings.requireRelay) {
							//RelayLog("Checking direct connections...");

							for (var k = checkNodes.Count - 1; k >= 0; k--) {
								var pair = checkNodes[k];

								var node = pair.Key;

								if (node.isHome) {
									continue;
								}

								//RelayLog("Check node: {0}", k);

								for (var l = FlightGlobals.Vessels.Count - 1; l >= 0; l--) {
									var otherVessel = FlightGlobals.Vessels[l];

									if (otherVessel == null) {
										continue;
									}

									if (otherVessel == v) {
										continue;
									}

									var type = otherVessel.vesselType;

									if (type == VesselType.Debris || type == VesselType.SpaceObject || type == VesselType.Unknown || type == VesselType.Flag) {
										continue;
									}

									if (otherVessel.connection == null || otherVessel.connection.Comm == null) {
										continue;
									}

									var otherComm = otherVessel.connection.Comm;

									if (otherComm.antennaRelay.power > 0) {
										continue;
									}

									//RelayLog("Antenna valid for vessel\n---- {0} ----", otherVessel.vesselName);

									if (_settings.requireMPL && !VesselUtilities.VesselHasModuleName("ModuleScienceLab", otherVessel)) {
										continue;
									}

									if (!VesselUtilities.VesselHasModuleName("ModuleScienceContainer", otherVessel)) {
										continue;
									}

									var dist = (otherComm.precisePosition - node.precisePosition).magnitude;

									if (isOccluded(node, otherComm, dist, net)) {
										continue;
									}

									var power = directConnection(node, otherComm, dist, source == node, pair.Value);

									if (power <= 0) {
										continue;
									}

									power = source.scienceCurve.Evaluate(power);

									//RelayLog("Vessel not occluded, with signal strength: {0:N2}", power);

									var flag = false;

									for (var m = connections.Count - 1; m >= 0; m--) {
										var connect = connections[m];

										if (connect.Key != otherVessel) {
											continue;
										}

										if (connect.Value < power + 1) {
											connections[m] = new KeyValuePair<Vessel, double>(connect.Key, power + 1);
										}

										flag = true;
										break;
									}

									for (var m = connections.Count - 1; m >= 0; m--) {
										var connect = connections[m];

										if (connect.Key != otherVessel) {
											continue;
										}

										break;
									}

									if (!flag) {
										//RelayLog("Adding direct connection vessel");
										connections.Add(new KeyValuePair<Vessel, double>(otherVessel, power + 1));
									}
								}
							}
						}
					}
				}
			}

			return connections;
		}

		private bool updateCommNode(List<KeyValuePair<CommNode, double>> nodes, CommNode newNode, double signal)
		{
			for (var i = nodes.Count - 1; i >= 0; i--) {
				var node = nodes[i];

				if (node.Key != newNode) {
					continue;
				}

				//RelayLog("Updating Comm Node - New Signal: {0:N2} - Old Value: {1:N2}", signal, node.Value);

				if (signal > node.Value) {
					nodes[i] = new KeyValuePair<CommNode, double>(node.Key, signal);
				}

				return true;
			}

			return false;
		}

		private bool isOccluded(CommNode a, CommNode b, double dist, CommNetwork net)
		{
			//RelayLog("Checking connection occlusion");

			bool? occlusion = null;

			try {
				occlusion = _occlusionMethod.Invoke(
					net,
					new object[] {a.precisePosition, a.occluder, b.precisePosition, b.occluder, dist}
				) as bool?;
			} catch (Exception e) {
				RelayLog("Error in assessing occlusion for science relay...\n{0}", e);
				return true;
			}

			if (occlusion == null) {
				return true;
			}

			return !(bool) occlusion;
		}

		private double directConnection(CommNode a, CommNode b, double dist, bool source, double strength)
		{
			//RelayLog("Checking direct connection strength");

			var plasmaMult = a.GetSignalStrengthMultiplier(b) * b.GetSignalStrengthMultiplier(a);

			double power = 0;

			if (source) {
				var range = CommNetScenario.RangeModel.GetNormalizedRange(a.antennaTransmit.power, b.antennaTransmit.power, dist);

				if (range > 0) {
					power = Math.Sqrt(a.antennaTransmit.rangeCurve.Evaluate(range) * b.antennaTransmit.rangeCurve.Evaluate(range));

					power *= plasmaMult;
				} else {
					range = CommNetScenario.RangeModel.GetNormalizedRange(a.antennaRelay.power, b.antennaTransmit.power, dist);

					if (range > 0) {
						power = Math.Sqrt(a.antennaRelay.rangeCurve.Evaluate(range) * b.antennaTransmit.rangeCurve.Evaluate(range));

						power *= plasmaMult;
					}
				}
			} else {
				var range = CommNetScenario.RangeModel.GetNormalizedRange(a.antennaRelay.power, b.antennaTransmit.power, dist);

				if (range > 0) {
					power = Math.Sqrt(a.antennaRelay.rangeCurve.Evaluate(range) * b.antennaTransmit.rangeCurve.Evaluate(range));

					power *= plasmaMult;
				}
			}

			return power * strength;
		}

		private void assignReflection()
		{
			try {
				_occlusionMethod = typeof(CommNetwork).GetMethod("TestOcclusion", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] {typeof(Vector3d), typeof(Occluder), typeof(Vector3d), typeof(Occluder), typeof(double)}, null);
			} catch (Exception e) {
				RelayLog("Error in assigning occlusion method; Science Relay may not be able to accurately determine vessel connectivity\n{0}", e);
			}

			_reflected = true;
		}

		private void CommNetConstellationCheck()
		{
			var assembly = AssemblyLoader.loadedAssemblies.FirstOrDefault(a => a.assembly.GetName().Name.StartsWith("CommNetConstellation"));

			_cnConstellationLoaded = assembly != null;

			//if (CNConstellationLoaded)
			//	RelayLog("CommNet Constellation addon detected; Science Relay disabling CommNet connection status integration");

			_cnConstellationChecked = true;
		}

		public static void RelayLog(string s, params object[] o)
		{
			Debug.Log(string.Format("[Science_Relay] " + s, o));
		}
	}
}