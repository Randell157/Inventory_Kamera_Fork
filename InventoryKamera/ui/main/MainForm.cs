using InventoryKamera.ui;
using Microsoft.WindowsAPICodePack.Dialogs;
using Newtonsoft.Json;
using NHotkey;
using NHotkey.WindowsForms;
using Octokit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using WindowsInput.Native;
using Application = System.Windows.Forms.Application;

namespace InventoryKamera
{
    // The Classic look: the 1.6 window. Scanning and the startup checks are shared with the other
    // looks (ScanSession, AppStartup); this window only shows and edits the settings.
    public partial class MainForm : Form, ILookWindow, IScanWindow
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        public MainForm()
        {
            // Blank Wanderer and mannequin names were already refilled by AppStartup.PrepareSettings,
            // before the boxes bind to the settings.
            InitializeComponent();

            // Leaving one of these boxes empty fills its default back in (not the Traveler's:
            // empty there means "read the name from the game").
            foreach (var (box, setting) in new[] { (wandererNameTextBox, "WandererName"), (textBox1, "Manequin1Name"), (textBox2, "Manequin2Name") })
                box.Leave += (s, e) => { if (string.IsNullOrWhiteSpace(box.Text)) box.Text = CustomNames.OrDefault(setting, box.Text); };

            Language_ComboBox.SelectedItem = "ENG";

            Text = $"Inventory Kamera V{LookContext.VersionText}";   // the version is logged by LookContext

            PointUserInterfaceHere();

            AddZeroMeansAllHints();
            // View -> Simple / Advanced / Classic and Zoom, between File and Options.
            menuStrip1.Items.Insert(menuStrip1.Items.IndexOf(fileToolStripMenuItem) + 1, LookMenu.Build(Look.Classic, this));
            DatabaseUpdateMenuItem.Text = "Updates…";
            UpdateMark.Attach(this, keysToolStripMenuItem, DatabaseUpdateMenuItem);
        }

        // The status, counters, images and error log of a scan show in this window's controls.
        private void PointUserInterfaceHere()
        {
            UserInterface.Init(
                GearPictureBox,
                ArtifactOutput_TextBox,
                CharacterName_PictureBox,
                CharacterLevel_PictureBox,
                new[] { CharacterTalent1_PictureBox, CharacterTalent2_PictureBox, CharacterTalent3_PictureBox },
                CharacterOutput_TextBox,
                WeaponsScannedCount_Label,
                WeaponsMax_Labell,
                ArtifactsScanned_Label,
                ArtifactsMax_Label,
                CharactersScanned_Label,
                ProgramStatus_Label,
                ErrorLog_TextBox,
                Navigation_Image);
        }

        // ILookWindow: this window is on screen again (or was zoomed). Its controls show the scan from now on;
        // the status line is redrawn at its new size.
        public void BecameShown()
        {
            PointUserInterfaceHere();
            UpdateKeyTextBoxes();   // Advanced may have changed them, and the scan speed
            int choice = ScanSpeeds.ChoiceOf(ScanSpeeds.Valid(Properties.Settings.Default.ScanSpeed));
            if (ScanSpeed_ComboBox.Items.Count > choice && ScanSpeed_ComboBox.SelectedIndex != choice) ScanSpeed_ComboBox.SelectedIndex = choice;
            UserInterface.RefitStatus();
            ManualExportButton.Enabled = ScanWindows.CanOpenOptimizer;   // a scan in another look may have ended
        }

        public void BecameHidden() { }

        // ILookWindow: the lookup tables were reloaded; re-check the custom-name boxes against them.
        public void LookupTablesReloaded()
        {
            foreach (var box in new[] { travelerNameTextBox, wandererNameTextBox, textBox1, textBox2 })
                ValidateCustomName(box, EventArgs.Empty);
        }

        // IScanWindow
        public Control Invoker => this;

        public void ScanStopped(bool hasData)
        {
            ManualExportButton.Invoke((MethodInvoker)delegate
            {
                ManualExportButton.Enabled = hasData;
            });
            MainForm_Activate();
        }

        #region "0 = all" hints

        // 0 means "all" for both of these settings, but that was only in a hover tooltip.
        // Say it in the labels, and show what the current number will do next to each box.
        private System.Windows.Forms.Label charactersHint, artifactPagesHint;

        private void AddZeroMeansAllHints()
        {
            label5.Text = "Characters to scan (0 = all)";
            SortByObtained.Text = "Artifact pages to scan (0 = all)";
            ZeroMeansAllTooltips.SetToolTip(label5, "0 scans every character. Another number scans that many, starting from the first in your list.");
            ZeroMeansAllTooltips.SetToolTip(SortByObtained, "0 scans every artifact. Another number scans that many pages of your newest artifacts.");

            charactersHint = AddHint(NumOfCharToScanControl);
            artifactPagesHint = AddHint(SortByObtainedControl);

            NumOfCharToScanControl.ValueChanged += (s, e) => UpdateZeroMeansAllHints();
            SortByObtainedControl.ValueChanged += (s, e) => UpdateZeroMeansAllHints();
            UpdateZeroMeansAllHints();
        }

        private System.Windows.Forms.Label AddHint(Control numberBox)
        {
            var hint = new System.Windows.Forms.Label
            {
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                Location = new Point(numberBox.Right + 4, numberBox.Top + 3),
            };
            Controls.Add(hint);
            hint.BringToFront();
            return hint;
        }

        private void UpdateZeroMeansAllHints()
        {
            int characters = (int)NumOfCharToScanControl.Value;
            charactersHint.Text = ZeroMeansAllHint(characters, "all characters", characters == 1 ? "first character" : $"first {characters} characters");

            int pages = (int)SortByObtainedControl.Value;
            artifactPagesHint.Text = ZeroMeansAllHint(pages, "all artifacts", pages == 1 ? "newest page" : $"newest {pages} pages");
        }

        internal static string ZeroMeansAllHint(int value, string all, string some) => "= " + (value == 0 ? all : some);

        #endregion "0 = all" hints

        private void MainForm_Load(object sender, EventArgs e)
        {
            // Settings upgrade, key repairs and the scan speed default: AppStartup.PrepareSettings.
            UpdateKeyTextBoxes();

            LoadScanSpeed();

            ProgramStatus_Label.Text = "";
            if (string.IsNullOrWhiteSpace(OutputPath_TextBox.Text))
            {
                OutputPath_TextBox.Text = Directory.GetCurrentDirectory() + @"\GenshinData";
            }

            // Someone upgrading from 1.6 is told once that the new looks exist.
            var settings = Properties.Settings.Default;
            if (Looks.ShowHint(settings.LooksHintShown, Look.Classic))
            {
                UserInterface.SetProgramStatus(Looks.HintText, UserInterface.HintColor);
                settings.LooksHintShown = true;
                settings.Save();
                Logger.Info("Showed the new-looks hint");
            }
        }

        private void UpdateKeyTextBoxes()
        {
            inventoryToolStripTextBox.Text = GameKeys.Display((Keys)Navigation.inventoryKey);
            characterToolStripTextBox.Text = GameKeys.Display((Keys)Navigation.characterKey);
            slot1StripTextBox.Text = GameKeys.Display((Keys)Navigation.slotOneKey);
        }

        // Yellow only when a box holds another character's name; its own default
        // ("Wanderer", "Manequin1"...) and an empty box stay white.
        private void ValidateCustomName(object sender, EventArgs e)
        {
            var textbox = sender as TextBox;
            if (textbox == null) return;

            string ownKey;
            if (textbox == travelerNameTextBox) ownKey = "traveler";
            else if (textbox == wandererNameTextBox) ownKey = "wanderer";
            else if (textbox == textBox1) ownKey = "manequin1";
            else if (textbox == textBox2) ownKey = "manequin2";
            else return;

            bool clash = GenshinProcesor.NameBelongsToAnotherCharacter(textbox.Text, ownKey);
            textbox.BackColor = clash ? Color.Yellow : Color.White;
            // One shared tooltip: Windows shows it while hovering and hides it on leaving,
            // so it can't stack (a new ToolTip per hover used to pile up copies).
            customNameToolTip.SetToolTip(textbox, clash
                ? $"{textbox.Text} already exists as a character's name.\n" +
                  "This may affect equipping items to characters and is not fully supported yet."
                : null);
        }

        // Created with the form (field initializers run before InitializeComponent, whose
        // ParentChanged events already call ValidateCustomName).
        private readonly ToolTip customNameToolTip = new ToolTip();

        private void StartButton_Clicked(object sender, EventArgs e)
        {
            ScanSession.Start(OutputPath_TextBox.Text, this);
        }

        // Flashes the taskbar and shows the end-of-scan summary (shared with Simple: ScanWindows).
        public void ScanEnded(string title, string message, MessageBoxIcon icon) =>
            ScanWindows.ShowScanEnded(this, title, message, icon);

        private void Github_Label_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(ForkRepository.Url);
        }

        private void Releases_Label_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(ForkRepository.ReleasesUrl);
        }

        private void IssuesPage_Label_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start(ForkRepository.IssuesUrl);
        }

        private void FileSelectButton_Click(object sender, EventArgs e)
        {
            var folder = ScanWindows.PickFolder(OutputPath_TextBox.Text);
            if (folder != null) OutputPath_TextBox.Text = folder;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            SaveSettings();
            ScanSession.RemoveHotkey();
        }

        private void SaveSettings()
        {
            Properties.Settings.Default.Save();
        }

        #region Scan speed drop-down

        private ToolTip scanSpeedToolTip;   // hovering the closed box or its heading: the current setting
        private ToolTip scanChoiceToolTip;  // pointing at a choice in the open list: that choice
        private int shownChoiceTip = -1;

        // Fills the drop-down and selects the saved speed (its first-launch default is set by
        // AppStartup.PrepareSettings: Auto for a new install, the old slider's speed for an upgrade).
        private void LoadScanSpeed()
        {
            var settings = Properties.Settings.Default;
            int step = ScanSpeeds.Valid(settings.ScanSpeed);

            scanSpeedToolTip = new ToolTip(components) { AutoPopDelay = 30000 };
            scanChoiceToolTip = new ToolTip(components);
            ScanSpeed_ComboBox.Items.Clear();
            for (int i = 0; i < ScanSpeeds.ChoiceCount; i++) ScanSpeed_ComboBox.Items.Add(ScanSpeeds.ItemText(ScanSpeeds.StepAt(i)));
            // The slider has one step per drop-down choice, in the same order (Auto, Fastest … Slowest).
            ScanSpeed_TrackBar.Maximum = ScanSpeeds.SliderMax;
            ScanSpeedStart_Label.Text = ScanSpeeds.SliderStartLabel;
            ScanSpeedEnd_Label.Text = ScanSpeeds.SliderEndLabel;
            ScanSpeedEnd_Label.Left = ScanSpeed_TrackBar.Right - 6 - ScanSpeedEnd_Label.PreferredWidth;
            ScanSpeed_ComboBox.SelectedIndex = ScanSpeeds.ChoiceOf(step);   // raises SelectedIndexChanged, which fills the hint, slider and tooltip
            ShowScanSpeedControl(settings.ScanSpeedControl);
        }

        private void ScanSpeed_ComboBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (ScanSpeed_ComboBox.SelectedIndex < 0) return;
            int step = ScanSpeeds.StepAt(ScanSpeed_ComboBox.SelectedIndex);
            Properties.Settings.Default.ScanSpeed = step;
            if (ScanSpeed_TrackBar.Value != ScanSpeed_ComboBox.SelectedIndex) ScanSpeed_TrackBar.Value = ScanSpeed_ComboBox.SelectedIndex;
            ScanSpeedHint_Label.Text = ScanSpeeds.HintText(step);
            UpdateScanSpeedHeading();
            string tip = ScanSpeeds.TooltipText(step);
            foreach (Control c in new Control[] { ScanSpeed_ComboBox, ScannerDelay_Label, ScanSpeedHint_Label, ScanSpeed_TrackBar, ScanSpeedStart_Label, ScanSpeedEnd_Label })
                scanSpeedToolTip.SetToolTip(c, tip);
        }

        // Dragging the slider, or clicking beside the knob (one step: LargeChange is 1), picks the same
        // choice in the drop-down, which saves it.
        private void ScanSpeed_TrackBar_Scroll(object sender, EventArgs e)
        {
            if (ScanSpeed_ComboBox.SelectedIndex != ScanSpeed_TrackBar.Value) ScanSpeed_ComboBox.SelectedIndex = ScanSpeed_TrackBar.Value;
        }

        // Options → Scan Speed Control → Drop-down / Slider. Both change the same saved speed.
        private void ScanSpeedControlMenuItem_Click(object sender, EventArgs e)
        {
            int control = sender == scanSpeedSliderMenuItem ? ScanSpeeds.Slider : ScanSpeeds.DropDown;
            Properties.Settings.Default.ScanSpeedControl = control;
            Properties.Settings.Default.Save();
            ShowScanSpeedControl(control);
        }

        private void ShowScanSpeedControl(int control)
        {
            bool slider = ScanSpeeds.IsSlider(control);
            ScanSpeed_ComboBox.Visible = ScanSpeedHint_Label.Visible = !slider;
            ScanSpeed_TrackBar.Visible = ScanSpeedStart_Label.Visible = ScanSpeedEnd_Label.Visible = slider;
            scanSpeedDropDownMenuItem.Checked = !slider;
            scanSpeedSliderMenuItem.Checked = slider;
            UpdateScanSpeedHeading();
        }

        // The drop-down names each choice itself; above the slider the heading names the step.
        private void UpdateScanSpeedHeading()
        {
            ScannerDelay_Label.Text = ScanSpeed_TrackBar.Visible && ScanSpeed_ComboBox.SelectedIndex >= 0
                ? ScanSpeeds.SliderHeading(ScanSpeeds.StepAt(ScanSpeed_ComboBox.SelectedIndex))
                : "Delay per item";
        }

        // Drawn by the form (DrawMode.OwnerDrawFixed) only so it can tell which choice is under the pointer:
        // while the list is open, Windows highlights that choice, and its tooltip is shown beside the list.
        private void ScanSpeed_ComboBox_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            e.DrawBackground();
            TextRenderer.DrawText(e.Graphics, ScanSpeed_ComboBox.Items[e.Index].ToString(), e.Font, e.Bounds, e.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            e.DrawFocusRectangle();

            bool inOpenList = ScanSpeed_ComboBox.DroppedDown && (e.State & DrawItemState.ComboBoxEdit) == 0;
            if (inOpenList && (e.State & DrawItemState.Selected) != 0 && e.Index != shownChoiceTip)
            {
                shownChoiceTip = e.Index;
                // Beside the list, level with the choice. The list opens right under the box.
                scanChoiceToolTip.Show(ScanSpeeds.TooltipText(ScanSpeeds.StepAt(e.Index), forChoice: true), ScanSpeed_ComboBox,
                    ScanSpeed_ComboBox.Width + 6, ScanSpeed_ComboBox.Height + e.Bounds.Top);
            }
        }

        private void ScanSpeed_ComboBox_DropDownClosed(object sender, EventArgs e)
        {
            scanChoiceToolTip.Hide(ScanSpeed_ComboBox);
            shownChoiceTip = -1;
        }

        #endregion Scan speed drop-down

        private void Exit_MenuItem_Click(object sender, EventArgs e)
        {
            Application.Exit();
        }

        // Options -> Inventory / Character Screen / Character Slot 1 key (shared with Advanced: GameKeys).
        private void OptionsMenuItem_KeyDown(object sender, KeyEventArgs e) => GameKeys.KeyDown(sender, e);

        // Options -> Updates... (1.7): the app and the game data in one window.
        private void DatabaseUpdateMenuItem_Click(object sender, EventArgs e) => UpdatesForm.ShowFor(this);


        private void ExportFolderMenuItem_Click(object sender, EventArgs e)
        {
            ScanWindows.OpenFolder(OutputPath_TextBox.Text);
        }

        // The update checks and the first download run once per launch from LookContext, whatever look is open.
        private void MainForm_Shown(object sender, EventArgs e) { }

        private void Export_Button_Click(object sender, EventArgs e)
        {
            ScanWindows.OpenOptimizer(new GOOD(ScanSession.Data));
        }

        private void MainForm_Activate()
        {
            BeginInvoke((MethodInvoker)delegate { Activate(); });
        }

        private void ErrorLog_Label_Click(object sender, EventArgs e)
        {
            Process.Start($@"logging");
        }

        private void updateExecutablesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new ExecutablesForm().Show();
        }
    }
}