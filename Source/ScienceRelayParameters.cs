#region license

/*The MIT License (MIT)

ScienceRelayParameters - In game settings for Science Transfer

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

using System.Reflection;

namespace ScienceRelay
{
	public class ScienceRelayParameters : GameParameters.CustomParameterNode
	{
		[GameParameters.CustomParameterUI("#autoLOC_ScienceRelay_Settings_MPL", autoPersistance = true)]
		public bool requireMPL = true;

		[GameParameters.CustomParameterUI("#autoLOC_ScienceRelay_Settings_MPLBoost", autoPersistance = true)]
		public bool requireMPLForBoost;

		[GameParameters.CustomParameterUI("#autoLOC_ScienceRelay_Settings_Relay", autoPersistance = true)]
		public bool requireRelay;

		[GameParameters.CustomParameterUI("#autoLOC_ScienceRelay_Settings_Warning", toolTip = "#autoLOC_ScienceRelay_Settings_Tooltip_Warning", autoPersistance = true)]
		public bool showTransmitWarning;

		[GameParameters.CustomParameterUI("#autoLOC_ScienceRelay_Settings_Boost", toolTip = "#autoLOC_ScienceRelay_Settings_Tooltip_Boost", autoPersistance = true)]
		public bool transmissionBoost = true;

		[GameParameters.CustomFloatParameterUI("#autoLOC_ScienceRelay_Settings_Penalty", toolTip = "#autoLOC_ScienceRelay_Settings_Tooltip_Penalty", asPercentage = true, minValue = 0, maxValue = 1, displayFormat = "N2", autoPersistance = true)]
		public float transmissionPenalty = 0.5f;

		public override GameParameters.GameMode GameMode => GameParameters.GameMode.SCIENCE | GameParameters.GameMode.CAREER;

		public override bool HasPresets => true;

		public override string Section => "DMagic Mods";

		public override string DisplaySection => "DMagic Mods";

		public override int SectionOrder => 2;

		public override string Title => "Science Relay";

		public override void SetDifficultyPreset(GameParameters.Preset preset)
		{
			switch (preset) {
				case GameParameters.Preset.Easy:
					transmissionBoost = true;
					requireMPLForBoost = false;
					requireMPL = false;
					requireRelay = false;
					transmissionPenalty = 0;
					break;
				case GameParameters.Preset.Normal:
					transmissionBoost = true;
					requireMPLForBoost = false;
					requireMPL = false;
					requireRelay = false;
					transmissionPenalty = 0.25f;
					break;
				case GameParameters.Preset.Moderate:
					transmissionBoost = true;
					requireMPLForBoost = true;
					requireMPL = false;
					requireRelay = true;
					transmissionPenalty = 0.5f;
					break;
				case GameParameters.Preset.Hard:
					transmissionBoost = true;
					requireMPLForBoost = true;
					requireMPL = true;
					requireRelay = true;
					transmissionPenalty = 0.75f;
					break;
				case GameParameters.Preset.Custom:
					break;
			}
		}

		public override bool Enabled(MemberInfo member, GameParameters parameters)
		{
			if (member.Name == "requireMPLForBoost") {
				return !requireMPL && transmissionBoost;
			}

			if (member.Name == "transmissionPenalty") {
				return transmissionBoost;
			}

			return base.Enabled(member, parameters);
		}
	}
}