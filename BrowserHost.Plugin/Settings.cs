﻿using BrowserHost.Common;
using Dalamud.Game.Command;
using Dalamud.Interface;
using Dalamud.Plugin;
using ImGuiNET;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace BrowserHost.Plugin
{
	class Settings : IDisposable
	{
		public event EventHandler<InlayConfiguration> InlayAdded;
		public event EventHandler<InlayConfiguration> InlayNavigated;
		public event EventHandler<InlayConfiguration> InlayDebugged;
		public event EventHandler<InlayConfiguration> InlayRemoved;

		public Configuration Config;

		private DalamudPluginInterface pluginInterface;

#if DEBUG
		private bool open = true;
#else
		private bool open = false;
#endif

		InlayConfiguration selectedInlay = null;
		private Timer saveDebounceTimer;

		public Settings(DalamudPluginInterface pluginInterface)
		{
			this.pluginInterface = pluginInterface;

			pluginInterface.UiBuilder.OnOpenConfigUi += (sender, args) => open = true;
			pluginInterface.CommandManager.AddHandler("/pbrowser", new CommandInfo((command, arguments) => open = true)
			{
				HelpMessage = "Open BrowserHost configuration pane.",
				ShowInHelp = true,
			});
		}

		public void Initialise()
		{
			// Running this in a thread to avoid blocking the plugin init with potentially expensive stuff
			Task.Run(() =>
			{
				Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

				// Hydrate any inlays in the config
				foreach (var inlayConfig in Config.Inlays)
				{
					InlayAdded?.Invoke(this, inlayConfig);
				}
			});
		}

		public void Dispose() { }

		private InlayConfiguration AddNewInlay()
		{
			var inlayConfig = new InlayConfiguration()
			{
				Guid = Guid.NewGuid(),
				Name = "New inlay",
				Url = "about:blank",
			};
			Config.Inlays.Add(inlayConfig);
			InlayAdded?.Invoke(this, inlayConfig);
			SaveSettings();

			return inlayConfig;
		}

		private void NavigateInlay(InlayConfiguration inlayConfig)
		{
			if (inlayConfig.Url == "") { inlayConfig.Url = "about:blank"; }
			InlayNavigated?.Invoke(this, inlayConfig);
		}

		private void ReloadInlay(InlayConfiguration inlayConfig) { NavigateInlay(inlayConfig); }

		private void DebugInlay(InlayConfiguration inlayConfig)
		{
			InlayDebugged?.Invoke(this, inlayConfig);
		}

		private void RemoveInlay(InlayConfiguration inlayConfig)
		{
			InlayRemoved?.Invoke(this, inlayConfig);
			Config.Inlays.Remove(inlayConfig);
			SaveSettings();
		}

		private void DebouncedSaveSettings()
		{
			saveDebounceTimer?.Dispose();
			saveDebounceTimer = new Timer(_ => SaveSettings(), null, 1000, Timeout.Infinite);
		}

		private void SaveSettings()
		{
			saveDebounceTimer?.Dispose();
			saveDebounceTimer = null;
			pluginInterface.SavePluginConfig(Config);
		}

		public void Render()
		{
			if (!open || Config == null) { return; }

			// Primary window container
			ImGui.SetNextWindowSizeConstraints(new Vector2(400, 300), new Vector2(9001, 9001));
			var windowFlags = ImGuiWindowFlags.None
				| ImGuiWindowFlags.NoScrollbar
				| ImGuiWindowFlags.NoScrollWithMouse
				| ImGuiWindowFlags.NoCollapse;
			ImGui.Begin("BrowserHost Settings", ref open, windowFlags);

			RenderPaneSelector();

			// Pane details
			var dirty = false;
			ImGui.SameLine();
			ImGui.BeginChild("details");
			if (selectedInlay == null)
			{
				dirty |= RenderGeneralSettings();
			}
			else
			{
				dirty |= RenderInlaySettings(selectedInlay);
			}
			ImGui.EndChild();

			if (dirty) { DebouncedSaveSettings(); }

			ImGui.End();
		}

		private void RenderPaneSelector()
		{
			// Selector pane
			ImGui.BeginGroup();
			ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));

			var selectorWidth = 100;
			ImGui.BeginChild("panes", new Vector2(selectorWidth, -ImGui.GetFrameHeightWithSpacing()), true);

			// General settings
			if (ImGui.Selectable($"General", selectedInlay == null))
			{
				selectedInlay = null;
			}

			// Inlay selector list
			ImGui.Dummy(new Vector2(0, 5));
			ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
			ImGui.Text("- Inlays -");
			ImGui.PopStyleVar();
			foreach (var inlayConfig in Config.Inlays)
			{
				if (ImGui.Selectable($"{inlayConfig.Name}##{inlayConfig.Guid}", selectedInlay == inlayConfig))
				{
					selectedInlay = inlayConfig;
				}
			}
			ImGui.EndChild();

			// Selector controls
			ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 0);
			ImGui.PushFont(UiBuilder.IconFont);

			var buttonWidth = selectorWidth / 2;
			if (ImGui.Button(FontAwesomeIcon.Plus.ToIconString(), new Vector2(buttonWidth, 0)))
			{
				selectedInlay = AddNewInlay();
			}

			ImGui.SameLine();
			if (selectedInlay != null)
			{
				if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0)))
				{
					var toRemove = selectedInlay;
					selectedInlay = null;
					RemoveInlay(toRemove);
				}
			}
			else
			{
				ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f);
				ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(buttonWidth, 0));
				ImGui.PopStyleVar();
			}

			ImGui.PopFont();
			ImGui.PopStyleVar(2);

			ImGui.EndGroup();
		}

		private bool RenderGeneralSettings()
		{
			var dirty = false;

			ImGui.Text("Select an inlay on the left to edit its settings.");

			// TODO: Flipping this should probably try to rebuild existing inlays
			var altFrameTransport = Config.FrameTransportMode == FrameTransportMode.BitmapBuffer;
			dirty |= ImGui.Checkbox("Use alternate frame transport", ref altFrameTransport);
			Config.FrameTransportMode = altFrameTransport
				? FrameTransportMode.BitmapBuffer
				: FrameTransportMode.SharedTexture;

			return dirty;
		}

		private bool RenderInlaySettings(InlayConfiguration inlayConfig)
		{
			var dirty = false;

			ImGui.PushID(inlayConfig.Guid.ToString());

			dirty |= ImGui.InputText("Name", ref inlayConfig.Name, 100);

			dirty |= ImGui.InputText("URL", ref inlayConfig.Url, 1000);
			if (ImGui.IsItemDeactivatedAfterEdit()) { NavigateInlay(inlayConfig); }

			var true_ = true;
			if (inlayConfig.ClickThrough) { ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 0.5f); }
			dirty |= ImGui.Checkbox("Locked", ref inlayConfig.ClickThrough ? ref true_ : ref inlayConfig.Locked);
			if (inlayConfig.ClickThrough) { ImGui.PopStyleVar(); }
			if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the inlay from being resized or moved. This is implicitly set by Click Through."); }

			ImGui.SameLine();
			dirty |= ImGui.Checkbox("Click Through", ref inlayConfig.ClickThrough);
			if (ImGui.IsItemHovered()) { ImGui.SetTooltip("Prevent the inlay from intecepting any mouse events."); }

			if (ImGui.Button("Reload")) { ReloadInlay(inlayConfig); }

			ImGui.SameLine();
			if (ImGui.Button("Open Dev Tools")) { DebugInlay(inlayConfig); }

			ImGui.PopID();

			return dirty;
		}
	}
}
