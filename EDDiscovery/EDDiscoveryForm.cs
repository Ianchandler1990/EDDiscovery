﻿/*
 * Copyright © 2015 - 2017 EDDiscovery development team
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not use this
 * file except in compliance with the License. You may obtain a copy of the License at
 *
 * http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software distributed under
 * the License is distributed on an "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF
 * ANY KIND, either express or implied. See the License for the specific language
 * governing permissions and limitations under the License.
 * 
 * EDDiscovery is not affiliated with Frontier Developments plc.
 */
using EliteDangerousCore.EDDN;
using EliteDangerousCore.EDSM;
using EDDiscovery.Forms;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using EliteDangerousCore;
using EliteDangerousCore.DB;
using EliteDangerousCore.JournalEvents;
using EliteDangerous.CompanionAPI;
using EDDiscovery.Icons;

namespace EDDiscovery
{
    public partial class EDDiscoveryForm : Forms.DraggableFormPos
    { 
        #region Variables

        private EDDiscoveryController Controller;
        private Actions.ActionController actioncontroller;

        public Actions.ActionController DEBUGGETAC { get { return actioncontroller; } }

        static public EDDConfig EDDConfig { get { return EDDConfig.Instance; } }
        public EDDTheme theme { get { return EDDTheme.Instance; } }

        public UserControls.IHistoryCursor PrimaryCursor { get { return tabControlMain.PrimaryTab.GetTravelGrid; } }
        public UserControls.UserControlContainerSplitter PrimarySplitter { get { return tabControlMain.PrimaryTab; } }

        public ScreenShots.ScreenShotConverter screenshotconverter;

        public EliteDangerousCore.CompanionAPI.CompanionAPIClass Capi { get; private set; } = new EliteDangerousCore.CompanionAPI.CompanionAPIClass();

        public EDDiscovery._3DMap.MapManager Map { get; private set; }

        Task checkInstallerTask = null;
        private bool themeok = true;

        BaseUtils.GitHubRelease newRelease;

        public PopOutControl PopOuts;

        #endregion

        #region Callbacks from us

        public event Action<Object> OnNewTarget;
        public event Action<Object, HistoryEntry, bool> OnNoteChanged;  // UI.Note has been updated attached to this note
        public event Action<List<ISystem>> OnNewCalculatedRoute;        // route plotter has a new one
        public event Action<List<string>> OnNewStarsForExpedition;      // add stars to expedition 
        public event Action<List<string>, bool> OnNewStarsForTrilat;    // add stars to trilat (false distance, true wanted)
        public event Action OnAddOnsChanged;                            // add on changed
        public event Action<int,string> OnEDSMSyncComplete;             // EDSM Sync has completed with this list of stars are newly created
        public event Action<int> OnEDDNSyncComplete;                    // Sync has completed
        public event Action<int,string> OnEGOSyncComplete;              // EGO Sync has completed with records on this list of stars

        #endregion

        #region IDiscoveryController interface
        #region Properties
        public HistoryList history { get { return Controller.history; } }
        public string LogText { get { return Controller.LogText; } }
        public bool PendingClose { get { return Controller.PendingClose; } }
        public GalacticMapping galacticMapping { get { return Controller.galacticMapping; } }
        #endregion

        #region Events - see the EDDiscoveryControl for meaning and context
        public event Action<HistoryList> OnHistoryChange { add { Controller.OnHistoryChange += value; } remove { Controller.OnHistoryChange -= value; } }
        public event Action<HistoryEntry, HistoryList> OnNewEntry { add { Controller.OnNewEntry += value; } remove { Controller.OnNewEntry -= value; } }
        public event Action<UIEvent> OnNewUIEvent { add { Controller.OnNewUIEvent += value; } remove { Controller.OnNewUIEvent -= value; } }
        public event Action<JournalEntry> OnNewJournalEntry { add { Controller.OnNewJournalEntry += value; } remove { Controller.OnNewJournalEntry -= value; } }
        public event Action<string, Color> OnNewLogEntry { add { Controller.OnNewLogEntry += value; } remove { Controller.OnNewLogEntry -= value; } }
        public event Action OnRefreshCommanders { add { Controller.OnRefreshCommanders += value; } remove { Controller.OnRefreshCommanders -= value; } }
        public event Action<EliteDangerousCore.CompanionAPI.CompanionAPIClass,HistoryEntry> OnNewCompanionAPIData;
        public event Action OnMapsDownloaded { add { Controller.OnMapsDownloaded += value; } remove { Controller.OnMapsDownloaded -= value; } }
        public event Action<bool> OnExpeditionsDownloaded { add { Controller.OnExpeditionsDownloaded += value; } remove { Controller.OnExpeditionsDownloaded -= value; } }

        #endregion

        #region Logging
        public void LogLine(string text) { Controller.LogLine(text); }
        public void LogLineHighlight(string text) { Controller.LogLineHighlight(text); }
        public void LogLineSuccess(string text) { Controller.LogLineSuccess(text); }
        public void LogLineColor(string text, Color color) { Controller.LogLineColor(text, color); }
        public void ReportProgress(int percent, string message) { Controller.ReportProgress(percent, message); }
        #endregion

        #region History
        public bool RefreshHistoryAsync()           // we only supply the basic refresh for the rest of the system..
        {
            return Controller.RefreshHistoryAsync();
        }
        public void RefreshDisplays() { Controller.RefreshDisplays(); }
        public void RecalculateHistoryDBs() { Controller.RecalculateHistoryDBs(); }
        #endregion

        #endregion

        #region Initialisation

        // note we do not do the traditional Initialize component here.. we wait for splash form to call it
        // and we need to tell the drag form pos our save name
        public EDDiscoveryForm()
        {
            RestoreFormPositionRegKey = "Form";

            Controller = new EDDiscoveryController(() => theme.TextBlockColor, () => theme.TextBlockHighlightColor, () => theme.TextBlockSuccessColor, a => BeginInvoke(a));
            Controller.OnNewEntrySecond += Controller_NewEntrySecond;       // called after UI updates themselves with NewEntry
            Controller.OnNewUIEvent += Controller_NewUIEvent;       // called if its an UI event
            Controller.OnBgSafeClose += Controller_BgSafeClose;
            Controller.OnFinalClose += Controller_FinalClose;
            Controller.OnInitialSyncComplete += Controller_InitialSyncComplete;
            Controller.OnRefreshCommanders += Controller_RefreshCommanders;
            Controller.OnRefreshComplete += Controller_RefreshComplete;
            Controller.OnRefreshStarting += Controller_RefreshStarting;
            Controller.OnReportProgress += Controller_ReportProgress;
            Controller.OnSyncComplete += Controller_SyncComplete;
            Controller.OnSyncStarting += Controller_SyncStarting;
            Controller.OnInitialisationComplete += Controller_InitialisationComplete;
        }

        public void Init(Action<string> msg)    // called from EDDApplicationContext .. continues on with the construction of the form
        {
            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " ED init");
            msg.Invoke("Modulating Shields");
            Controller.Init();
            PanelInformation.InitIcons();

            // Some components require the controller to be initialized
            // obsolete remove IconSet.SetPanelImageListGetter(PanelInformation.GetPanelImages);
            InitializeComponent();

            panelToolBar.HiddenMarkerWidth = 200;
            panelToolBar.SecondHiddenMarkerWidth = 60;
            panelToolBar.PinState = SQLiteConnectionUser.GetSettingBool("ToolBarPanelPinState", true);

            label_version.Text = EDDOptions.Instance.VersionDisplayString;

            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " Load popouts, themes, init controls");
            PopOuts = new PopOutControl(this);

            msg.Invoke("Repairing Canopy");
            theme.LoadThemes();                                         // default themes and ones on disk loaded

            if (!EDDOptions.Instance.NoTheme)
                themeok = theme.RestoreSettings();                                    // theme, remember your saved settings

            // open all the major tabs except the built in ones
            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " Creating major tabs Now");
            MaterialCommodityDB.SetUpInitialTable();


            // Tab reset Setup

            if (EDDOptions.Instance.TabsReset)
            {
                SQLiteConnectionUser.DeleteKey("GridControlWindows%");              // these hold the grid/splitter control values for all windows
                SQLiteConnectionUser.DeleteKey("SplitterControlWindows%");          // wack them so they start empty.
                SQLiteConnectionUser.DeleteKey("SavedPanelInformation.%");          // and delete the pop out history
            }

            //Make sure the primary splitter is set up..

            string primarycontrolname = "SplitterControlWindows";                   // primary name for first splitter
            string splitctrl = SQLiteConnectionUser.GetSettingString(primarycontrolname, "");

            if (splitctrl == "")       // never set, or wiped, reset.. if previous system had the IDs, use them, else use defaults
            {
                string typeprefix = EDDOptions.Instance.TabsReset ? "?????" : "TravelControl";      // if we have a tab reset, look up a nonsense name, to give default

                int enum_bottom = SQLiteDBClass.GetSettingInt(typeprefix + "BottomTab", (int)(PanelInformation.PanelIDs.Scan));
                int enum_bottomright = SQLiteDBClass.GetSettingInt(typeprefix + "BottomRightTab", (int)(PanelInformation.PanelIDs.Log));
                int enum_middleright = SQLiteDBClass.GetSettingInt(typeprefix + "MiddleRightTab", (int)(PanelInformation.PanelIDs.StarDistance));
                int enum_topright = SQLiteDBClass.GetSettingInt(typeprefix + "TopRightTab", (int)(PanelInformation.PanelIDs.SystemInformation));

                string ctrl = "V(0.75, H(0.6, U'0,1006',U'1," + enum_bottom.ToStringInvariant() + "')," +
                                "H(0.5, U'2," + enum_topright.ToStringInvariant() + "', " +
                                "H(0.25,U'3," + enum_middleright.ToStringInvariant() + "',U'4," + enum_bottomright + "')) )";

                SQLiteConnectionUser.PutSettingString(primarycontrolname, ctrl);
            }

            tabControlMain.MinimumTabWidth = 32;
            tabControlMain.CreateTabs(this, EDDOptions.Instance.TabsReset, "0, -1,0, 26,0, 27,0, 29,0, 34,0");      // numbers from popouts, which are FIXED!

            PanelInformation.PanelIDs[] pids = PanelInformation.GetPanelIDs();      // only user panels

            foreach(PanelInformation.PanelIDs pid in pids)
            {
                ToolStripMenuItem tsmi = PanelInformation.MakeToolStripMenuItem(pid, 
                    (s, e) => tabControlMain.AddTab((PanelInformation.PanelIDs)((s as ToolStripMenuItem).Tag), tabControlMain.LastTabClicked));

                if (tsmi != null)
                    addTabToolStripMenuItem.DropDownItems.Add(tsmi);
            }

            removeTabToolStripMenuItem.Click += (s, e) => tabControlMain.RemoveTab(tabControlMain.LastTabClicked);
            renameTabToolStripMenuItem.Click += (s, e) => 
            {
                string newvalue = ExtendedControls.PromptSingleLine.ShowDialog(this, "Name:", tabControlMain.TabPages[tabControlMain.LastTabClicked].Text, 
                                "Rename Tab", this.Icon, false, "Enter a new name for the tab");
                if (newvalue != null)
                    tabControlMain.RenameTab(tabControlMain.LastTabClicked, newvalue.Replace(";", "_"));
            };

            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " Map manager");
            Map = new EDDiscovery._3DMap.MapManager(this);

            this.TopMost = EDDConfig.KeepOnTop;

            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " Audio");

            msg.Invoke("Activating Sensors");

            actioncontroller = new Actions.ActionController(this, Controller, this.Icon);

            actioncontroller.ReLoad();          // load system up here

            screenshotconverter = new ScreenShots.ScreenShotConverter(this);

            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " Theming");
            ApplyTheme();

            notifyIcon1.Visible = EDDConfig.UseNotifyIcon;

            SetUpLogging();

            EDSMJournalSync.SentEvents = (count,list) =>              // Sync thread finishing, transfers to this thread, then runs the callback and the action..
            {
                this.BeginInvoke((MethodInvoker)delegate
                {
                    System.Diagnostics.Debug.Assert(Application.MessageLoop);
                    OnEDSMSyncComplete?.Invoke(count,list);
                    ActionRun(Actions.ActionEventEDList.onEDSMSync, null, new Conditions.ConditionVariables(new string[] { "EventStarList", list, "EventCount", count.ToStringInvariant() }));
                });
            };

            EDDNSync.SentEvents = (count) =>              // Sync thread finishing, transfers to this thread, then runs the callback and the action..
            {
                this.BeginInvoke((MethodInvoker)delegate
                {
                    System.Diagnostics.Debug.Assert(Application.MessageLoop);
                    OnEDDNSyncComplete?.Invoke(count);
                    ActionRun(Actions.ActionEventEDList.onEDDNSync, null, new Conditions.ConditionVariables(new string[] { "EventCount", count.ToStringInvariant() }));
                });
            };

            EliteDangerousCore.EGO.EGOSync.SentEvents = (count,list) =>              // Sync thread finishing, transfers to this thread, then runs the callback and the action..
            {
                this.BeginInvoke((MethodInvoker)delegate
                {
                    System.Diagnostics.Debug.Assert(Application.MessageLoop);
                    OnEGOSyncComplete?.Invoke(count,list);
                    ActionRun(Actions.ActionEventEDList.onEGOSync, null, new Conditions.ConditionVariables(new string[] { "EventStarList", list, "EventCount", count.ToStringInvariant() }));
                });
            };

            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " Finish ED Init");

            Controller.InitComplete();
        }

        // OnLoad is called the first time the form is shown, before OnShown or OnActivated are called
        private void EDDiscoveryForm_Load(object sender, EventArgs e)
        {
            try
            {
                Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " EDF Load");

                Controller.PostInit_Loaded();

                ShowInfoPanel("Loading. Please wait!", true);

                tabControlMain.LoadTabs();

                if (EDDOptions.Instance.ActionButton)
                {
                    buttonReloadActions.Visible = true;
                }

                Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " EDF load complete");
            }
            catch (Exception ex)
            {
                MessageBox.Show("EDDiscoveryForm_Load exception: " + ex.Message + "\n" + "Trace: " + ex.StackTrace);
            }
        }

        // OnShown is called every time Show is called
        private void EDDiscoveryForm_Shown(object sender, EventArgs e)
        {
            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " EDF shown");
            Controller.PostInit_Shown();

            if (!themeok)
            {
                Controller.LogLineHighlight("The theme stored has missing colors or other missing information");
                Controller.LogLineHighlight("Correct the missing colors or other information manually using the Theme Editor in Settings");
            }

            actioncontroller.onStartup();

            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " EDF shown complete");

            // Form is fully loaded, we can do tab actions now

            tabControlMain.SelectedIndexChanged += (snd, ea) => { ActionRun(Actions.ActionEventEDList.onTabChange, null, new Conditions.ConditionVariables("TabName", tabControlMain.TabPages[tabControlMain.SelectedIndex].Text)); };

            actioncontroller.CheckWarn();
        }

        private void EDDiscoveryForm_Resize(object sender, EventArgs e)
        {
            // We may be getting called by this.ResumeLayout() from InitializeComponent().
            if (EDDConfig != null && FormShownOnce)
            {
                if (EDDConfig.UseNotifyIcon && EDDConfig.MinimizeToNotifyIcon)
                {
                    if (FormWindowState.Minimized == WindowState)
                        Hide();
                    else if (!Visible)
                        Show();
                }

                notifyIconMenu_Open.Enabled = FormWindowState.Minimized == WindowState;
            }
        }

        #endregion

        #region New Installer

        private Task CheckForNewInstallerAsync()
        {
            return Task.Factory.StartNew(() =>
            {
#if !DEBUG
                CheckForNewinstaller();
#endif
            });
        }

        private bool CheckForNewinstaller()
        {
            try
            {

                BaseUtils.GitHubClass github = new BaseUtils.GitHubClass(EDDiscovery.Properties.Resources.URLGithubDownload, LogLine);

                BaseUtils.GitHubRelease rel = github.GetLatestRelease();

                if (rel != null)
                {
                    var currentVersion = System.Reflection.Assembly.GetExecutingAssembly().GetVersionString();
                    var releaseVersion = rel.ReleaseVersion;

                    Version v1 = new Version(releaseVersion);
                    Version v2 = new Version(currentVersion);

                    if (v1.CompareTo(v2) > 0) // Test if newer installer exists:
                    {
                        newRelease = rel;
                        this.BeginInvoke(new Action(() => Controller.LogLineHighlight("New EDDiscovery installer available: " + rel.ReleaseName)));
                        this.BeginInvoke(new Action(() => ShowInfoPanel("New Release Available!", true)));
                        return true;
                    }
                }
            }
            catch (Exception)
            {

            }

            return false;
        }

        #endregion

        #region Tabs - most code now in MajorTabControl.cs  (mostly) Only UI code left.

        public void AddTab(PanelInformation.PanelIDs id, int tabindex = 0) // negative means from the end.. -1 is one before end
        {
            tabControlMain.AddTab(id, tabindex);
        }

        public bool SelectTabPage(string name)
        {
            return tabControlMain.SelectTabPage(name);
        }

        private void panelTabControlBack_MouseDown(object sender, MouseEventArgs e)     // click on the empty space of the tabs.. backed up by the panel
        {
            if (e.Button == MouseButtons.Right)
            {
                tabControlMain.ClearLastTab();      // this sets LastTab to -1, which thankfully means insert at last but one position to the AddTab function
                contextMenuStripTabs.Show(tabControlMain.PointToScreen(e.Location));
            }
        }

        private void tabControlMain_MouseClick(object sender, MouseEventArgs e)     // click on one of the tab buttons
        {
            if (tabControlMain.LastTabClicked >= 0)
            {
                if (e.Button == MouseButtons.Right)
                {
                    Point p = tabControlMain.PointToScreen(e.Location);
                    p.Offset(0, -8);
                    contextMenuStripTabs.Show(p);
                }
                else if (e.Button == MouseButtons.Middle && !IsNonRemovableTab(tabControlMain.LastTabClicked))
                {
                    tabControlMain.RemoveTab(tabControlMain.LastTabClicked);
                }
            }
        }

        private void ContextMenuStripTabs_Opening(object sender, CancelEventArgs e)
        {
            int n = tabControlMain.LastTabClicked;
            bool validtab = n >= 0 && n < tabControlMain.TabPages.Count;   // sanity check

            removeTabToolStripMenuItem.Enabled = validtab && !IsNonRemovableTab(n);
            renameTabToolStripMenuItem.Enabled = validtab && !(tabControlMain.TabPages[n].Controls[0] is UserControls.UserControlPanelSelector);
        }

        private bool IsNonRemovableTab(int n)
        {
            bool uch = Object.ReferenceEquals(tabControlMain.TabPages[n].Controls[0], tabControlMain.PrimaryTab);
            bool sel = tabControlMain.TabPages[n].Controls[0] is UserControls.UserControlPanelSelector;
            return uch || sel;
        }

        private void EDDiscoveryForm_MouseDown(object sender, MouseEventArgs e)     // use the form to detect the click on the empty tab area.. it passes thru
        {
            if (e.Button == MouseButtons.Right && e.Y >= tabControlMain.Top)
            {
                tabControlMain.ClearLastTab();      // this sets LastTab to -1, which thankfully means insert at last but one position to the AddTab function
                Point p = this.PointToScreen(e.Location);
                p.Offset(0, -8);
                contextMenuStripTabs.Show(p);
            }
        }

        #endregion

        #region Themeing

        public void ApplyTheme()
        {
            panel_close.Visible = !theme.WindowsFrame;
            panel_minimize.Visible = !theme.WindowsFrame;
            label_version.Visible = !theme.WindowsFrame;

            this.Text = "EDDiscovery " + label_version.Text;            // note in no border mode, this is not visible on the title bar but it is in the taskbar..

            theme.ApplyToForm(this);

            labelInfoBoxTop.Location = new Point(label_version.Right + 16, labelInfoBoxTop.Top);

            Controller.RefreshDisplays();
        }

#endregion

#region EDSM and EDDB syncs code

        private void edsmRefreshTimer_Tick(object sender, EventArgs e)
        {
            Controller.AsyncPerformSync();
        }

        public void ForceEDSMEDDBFullRefresh()
        {
            SystemClassEDSM.ForceEDSMFullUpdate();
            EliteDangerousCore.EDDB.SystemClassEDDB.ForceEDDBFullUpdate();
            Controller.AsyncPerformSync(true, true);
        }

#endregion

#region Controller event handlers 
        private void Controller_InitialSyncComplete()
        {
            screenshotconverter.Start();

            ShowInfoPanel("", false);

            checkInstallerTask = CheckForNewInstallerAsync();
        }

        private void Controller_SyncStarting()      
        {
            edsmRefreshTimer.Enabled = false;
        }

        private void Controller_SyncComplete()
        {
            edsmRefreshTimer.Enabled = true;
        }

        private void Controller_RefreshStarting()
        {
            RefreshButton(false);
            actioncontroller.ActionRun(Actions.ActionEventEDList.onRefreshStart);
        }

        private void Controller_RefreshCommanders()
        {
            LoadCommandersListBox();             // in case a new commander has been detected
        }

        private void Controller_InitialisationComplete()
        {
            if (EDDConfig.AutoLoadPopOuts && EDDOptions.Instance.NoWindowReposition == false)
                PopOuts.LoadSavedPopouts();  //moved from initial load so we don't open these before we can draw them properly
        }

        private void Controller_RefreshComplete()
        {

            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " Refresh complete");

            RefreshButton(true);
            actioncontroller.ActionRunOnRefresh();



            if (!Capi.IsCommanderLoggedin(EDCommander.Current.Name))
            {
                Capi.Logout();

                bool isdisabled;
                bool isconfirmed = EliteDangerousCore.CompanionAPI.CompanionCredentials.CredentialState(EDCommander.Current.Name , out isdisabled) == EliteDangerousCore.CompanionAPI.CompanionCredentials.State.CONFIRMED;

                if (isconfirmed )
                {
                    if ( isdisabled)
                    {
                        LogLine("Companion API is disabled in commander settings");
                    }
                    else
                    {
                        try
                        {
                            Capi.LoginAs(EDCommander.Current.Name);
                            LogLine("Logged into Companion API");
                        }
                        catch (Exception ex)
                        {
                            LogLineHighlight("Companion API log in failed: " + ex.Message);
                            if (!(ex is EliteDangerousCore.CompanionAPI.CompanionAppException))
                                LogLineHighlight(ex.StackTrace);
                        }
                    }
                }
            }

            if (Capi.LoggedIn)
            {
                try
                {
                    Capi.GetProfile();
                    OnNewCompanionAPIData?.Invoke(Capi, null);
                }
                catch (Exception ex)
                {
                    LogLineHighlight("Companion API get failed: " + ex.Message);
                    if (!(ex is EliteDangerousCore.CompanionAPI.CompanionAppException))
                        LogLineHighlight(ex.StackTrace);
                }
            }

            Debug.WriteLine(BaseUtils.AppTicks.TickCount100 + " Refresh complete finished");
        }

        private void Controller_NewEntrySecond(HistoryEntry he, HistoryList hl)         // called after all UI's have had their chance
        {
            actioncontroller.ActionRunOnEntry(he, Actions.ActionEventEDList.NewEntry(he));

            // all notes committed
            SystemNoteClass.CommitDirtyNotes((snc) => { if (EDCommander.Current.SyncToEdsm && snc.FSDEntry) EDSMClass.SendComments(snc.SystemName, snc.Note, snc.EdsmId, he.Commander); });

            if ( he.EntryType == JournalTypeEnum.Docked )
            {
                if (Capi.IsCommanderLoggedin(EDCommander.Current.Name))
                {
                    // hang over from rares indenting.
                    {
                        System.Diagnostics.Debug.WriteLine("Commander " + EDCommander.Current.Name + " in CAPI");
                        try
                        {
                            Capi.GetProfile();
                            CMarket market = Capi.GetMarket();

                            JournalDocked dockevt = he.journalEntry as JournalDocked;

                            if (!Capi.Profile.Cmdr.docked)
                            {
                                LogLineHighlight("CAPI not docked. Server API lagging!");
                            }
                            else if (!dockevt.StarSystem.Equals(Capi.Profile.CurrentStarSystem.name))
                            {
                                LogLineHighlight("CAPI profileSystemRequired is " + dockevt.StarSystem + ", profile station is " + Capi.Profile.CurrentStarSystem.name);
                            }
                            else if (!dockevt.StationName.Equals(Capi.Profile.StarPort.name))
                            {
                                LogLineHighlight("CAPI profileStationRequired is " + dockevt.StationName + ", profile station is " + Capi.Profile.StarPort.name);
                            }
                            else if (!dockevt.StationName.Equals(market.name))
                            {
                                LogLineHighlight("CAPI stationname  " + dockevt.StationName + ",´market station is " + market.name);
                            }
                            else
                            {
                                JournalEDDCommodityPrices entry = JournalEntry.AddEDDCommodityPrices(EDCommander.Current.Nr, he.journalEntry.EventTimeUTC.AddSeconds(1), Capi.Profile.StarPort.name, Capi.Profile.StarPort.faction, market.jcommodities);
                                if (entry != null)
                                {
                                    Controller.NewEntry(entry);
                                    OnNewCompanionAPIData?.Invoke(Capi, he);

                                    if (EDCommander.Current.SyncToEddn)
                                        SendPricestoEDDN(he, market);           // synchronous, but only done on docking, not worried.

                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogLineHighlight("Companion API get failed: " + ex.Message);
                        }
                    }
                }
            }

            if (he.IsFSDJump)
            {
                int count = history.GetVisitsCount(he.System.Name);
                LogLine(string.Format("Arrived at system {0} Visit No. {1}", he.System.Name, count));
                System.Diagnostics.Trace.WriteLine("Arrived at system: " + he.System.Name + " " + count + ":th visit.");
            }

            if (EDCommander.Current.SyncToEdsm)
            {
                EDSMJournalSync.SendEDSMEvents(LogLine, he);
            }

            if (EDDNClass.IsEDDNMessage(he.EntryType,he.EventTimeUTC) && he.AgeOfEntry() < TimeSpan.FromDays(1.0) && EDCommander.Current.SyncToEddn == true)
            {
                EDDNSync.SendEDDNEvents(LogLine, he);
            }

            if (he.EntryType == JournalTypeEnum.Scan && EDCommander.Current.SyncToEGO)
            {
                EliteDangerousCore.EGO.EGOSync.SendEGOEvents(LogLine, he);
            }
        }

        private void Controller_NewUIEvent(UIEvent uievent)      
        {
            Conditions.ConditionVariables cv = new Conditions.ConditionVariables();

            string prefix = "EventClass_";
            cv.AddPropertiesFieldsOfClass(uievent, prefix, new Type[] { typeof(System.Drawing.Icon), typeof(System.Drawing.Image), typeof(System.Drawing.Bitmap), typeof(Newtonsoft.Json.Linq.JObject) }, 5);
            cv[prefix+"UIDisplayed"] = EDDConfig.ShowUIEvents ? "1" : "0";
            actioncontroller.ActionRun(Actions.ActionEventEDList.onUIEvent, cv);
            actioncontroller.ActionRun(Actions.ActionEventEDList.EliteUIEvent(uievent), cv); 
        }

        private void SendPricestoEDDN(HistoryEntry he, CMarket market)
        {
            try
            {
                EliteDangerousCore.EDDN.EDDNClass eddn = new EliteDangerousCore.EDDN.EDDNClass();

                eddn.commanderName = he.Commander.EdsmName;
                if (string.IsNullOrEmpty(eddn.commanderName))
                     eddn.commanderName = Capi.Credentials.Commander;

                if (he.Commander.Name.StartsWith("[BETA]", StringComparison.InvariantCultureIgnoreCase) || he.IsBetaMessage)
                    eddn.isBeta = true;

                JObject msg = eddn.CreateEDDNCommodityMessage(market.commodities, Capi.Profile.CurrentStarSystem.name, Capi.Profile.StarPort.name, market.id, DateTime.UtcNow);

                if (msg != null)
                {
                    LogLine($"Sent {he.EntryType.ToString()} event to EDDN ({he.EventSummary})");
                    if (eddn.PostMessage(msg))
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                LogLineHighlight("EDDN: Send commodities prices failed: " + ex.Message);
            }

        }

        private void Controller_ReportProgress(int percentComplete, string message)
        {
            if (!Controller.PendingClose)
            {
                if (percentComplete >= 0)
                {
                    this.toolStripProgressBar1.Visible = true;
                    this.toolStripProgressBar1.Value = percentComplete;
                }
                else
                {
                    this.toolStripProgressBar1.Visible = false;
                }

                this.toolStripStatusLabel1.Text = message;
            }
        }

#endregion

#region Closing

        private void EDDiscoveryForm_FormClosing(object sender, FormClosingEventArgs e)     // when user asks for a close
        {
            edsmRefreshTimer.Enabled = false;
            if (!Controller.ReadyForFinalClose)
            {
                e.Cancel = true;
                ShowInfoPanel("Closing, please wait!", true);
                actioncontroller.ActionRun(Actions.ActionEventEDList.onShutdown);
                Controller.Shutdown();
            }
        }

        private void Controller_BgSafeClose()       // run in thread..
        {
            actioncontroller.HoldTillProgStops();
        }

        private void Controller_FinalClose()        // run in UI, when controller finishes close
        {
            // send any dirty notes.  if they are, the call back gets called. If we have EDSM sync on, and its an FSD entry, send it
            SystemNoteClass.CommitDirtyNotes((snc) => { if (EDCommander.Current.SyncToEdsm && snc.FSDEntry) EDSMClass.SendComments(snc.SystemName, snc.Note, snc.EdsmId); });

            screenshotconverter.SaveSettings();
            SQLiteDBClass.PutSettingBool("ToolBarPanelPinState", panelToolBar.PinState);

            theme.SaveSettings(null);

            tabControlMain.CloseTabList();      // close and save tab list

            if (EDDConfig.AutoSavePopOuts)      // must do after settings have saved state
                PopOuts.SaveCurrentPopouts();

            notifyIcon1.Visible = false;

            actioncontroller.CloseDown();

            Close();
            Application.Exit();
        }
     
#endregion

#region Buttons, Mouse, Menus, NotifyIcon

        public void ShowInfoPanel(string message, bool visible)
        {
            labelInfoBoxTop.Text = message;
            labelInfoBoxTop.Visible = visible;
        }

        private void buttonReloadActions_Click(object sender, EventArgs e)
        {
            actioncontroller.ReLoad();
            actioncontroller.CheckWarn();
            actioncontroller.onStartup();
         }

        private void sendUnsyncedEGOScansToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<HistoryEntry> hlsyncunsyncedlist = Controller.history.FilterByScanNotEGOSynced;        // first entry is oldest
            EliteDangerousCore.EGO.EGOSync.SendEGOEvents(LogLine, hlsyncunsyncedlist);
        }

        private void frontierForumThreadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Properties.Resources.URLProjectEDForumPost);
        }

        private void eDDiscoveryHomepageToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Properties.Resources.URLProjectWiki);
        }

        private void settingsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            tabControlMain.EnsureMajorTabIsPresent(PanelInformation.PanelIDs.Settings, true);
        }


        private void showLogfilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            try
            {
                EDCommander cmdr = EDCommander.Current;

                if (cmdr != null)
                {
                    string cmdrfolder = cmdr.JournalDir;
                    if (cmdrfolder.Length < 1)
                        cmdrfolder = EDJournalClass.GetDefaultJournalDir();
                    Process.Start(cmdrfolder);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Show log files exception: " + ex.Message);
            }
        }

        private void show2DMapsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Open2DMap();
        }

        private void show3DMapsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Open3DMap(PrimaryCursor.GetCurrentHistoryEntry);
        }

        private void forceEDDBUpdateToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!EDDConfig.Instance.EDSMEDDBDownload)
                ExtendedControls.MessageBoxTheme.Show(this, "Star Data download is disabled. Use Settings to reenable it");
            else if (!Controller.AsyncPerformSync(eddbsync: true))      // we want it to have run, to completion, to allow another go..
                ExtendedControls.MessageBoxTheme.Show(this, "Synchronisation to databases is in operation or pending, please wait");
        }

        private void syncEDSMSystemsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!EDDConfig.Instance.EDSMEDDBDownload)
                ExtendedControls.MessageBoxTheme.Show(this, "Star Data download is disabled. Use Settings to reenable it");
            else if (ExtendedControls.MessageBoxTheme.Show(this, "This can take a considerable amount of time and bandwidth" + Environment.NewLine + "Confirm you want to do this?", "EDSM Download Warning", MessageBoxButtons.OKCancel, MessageBoxIcon.Asterisk)  == DialogResult.OK )
            {
                if (!Controller.AsyncPerformSync(edsmfullsync: true))      // we want it to have run, to completion, to allow another go..
                    ExtendedControls.MessageBoxTheme.Show(this, "Synchronisation to databases is in operation or pending, please wait");
            }
        }

        private void gitHubToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Properties.Resources.URLProjectGithub);
        }

        private void reportIssueIdeasToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Properties.Resources.URLProjectFeedback);
        }

        /// <summary>
        /// The settings panel check box for 'Use notification area icon' has changed.
        /// </summary>
        /// <param name="useNotifyIcon">Whether or not the setting is enabled.</param>
        internal void useNotifyIconChanged(bool useNotifyIcon)
        {
            notifyIcon1.Visible = useNotifyIcon;
            if (!useNotifyIcon && !Visible)
                Show();
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        public void AboutBox(Form parent = null)
        {
            AboutForm frm = new AboutForm();
            frm.ShowDialog(parent ?? this);
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            AboutBox(this);
        }

        private void eDDiscoveryChatDiscordToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Process.Start(Properties.Resources.URLProjectDiscord);
        }

        private void howToRunInSafeModeToResetVariousParametersToolStripMenuItem_Click(object sender, EventArgs e)
        {
            ExtendedControls.MessageBoxTheme.Show(this, "To start in safe mode, exit the program, hold down the shift key" + Environment.NewLine +
                            "and double click on the EDD program icon.  You will then be in the safe mode dialog." + Environment.NewLine +
                            "You can reset various parameters and move the data bases to other locations.",
                            "How to run safe mode", MessageBoxButtons.OK, MessageBoxIcon.Information);

        }

        private void showAllInTaskBarToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PopOuts.ShowAllPopOutsInTaskBar();
        }

        private void turnOffAllTransparencyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            PopOuts.MakeAllPopoutsOpaque();
        }

        private void clearEDSMIDAssignedToAllRecordsForCurrentCommanderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ExtendedControls.MessageBoxTheme.Show(this, "Confirm you wish to reset the assigned EDSM ID\r\n" +
                                "to all the current commander history entries,\r\n" +
                                " and clear all the assigned EDSM IDs in all your notes for all commanders\r\n\r\n" +
                                "This will not change your history, but when you next refresh, \r\n" +
                                "it will try and reassign EDSM systems to your history and notes.\r\n" +
                                "Use only if you think that the assignment of EDSM systems to entries is grossly wrong," +
                                "or notes are going missing\r\n" +
                                "\r\n" +
                                "You can manually change one EDSM assigned system by right clicking\r\n" +
                                "on the travel history and selecting the option"
                                , "WARNING", MessageBoxButtons.OKCancel) == DialogResult.OK)
            {
                JournalEntry.ClearEDSMID(EDCommander.CurrentCmdrID);
                SystemNoteClass.ClearEDSMID();
                Controller.RefreshHistoryAsync();
            }

        }


        private void paneleddiscovery_Click(object sender, EventArgs e)
        {
            AboutBox(this);
        }

        private void read21AndFormerLogFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Read21Folders(false);
        }

        private void read21AndFormerLogFiles_forceReloadLogsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Read21Folders(true);
        }

        private void Read21Folders(bool force)
        {
            if (Controller.history.CommanderId >= 0)
            {
                EDCommander cmdr = EDCommander.Current;
                if (cmdr != null)
                {
                    FolderBrowserDialog dirdlg = new FolderBrowserDialog();
                    DialogResult dlgResult = dirdlg.ShowDialog(this);

                    if (dlgResult == DialogResult.OK)
                    {
                        string logpath = dirdlg.SelectedPath;

                        Controller.RefreshHistoryAsync(netlogpath: logpath, forcenetlogreload: force, currentcmdr: cmdr.Nr);
                    }
                }
            }
        }

        private void dEBUGResetAllHistoryToFirstCommandeToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ExtendedControls.MessageBoxTheme.Show(this, "Confirm you wish to reset all history entries to the current commander", "WARNING", MessageBoxButtons.OKCancel) == DialogResult.OK)
            {
                JournalEntry.ResetCommanderID(-1, EDCommander.CurrentCmdrID);
                Controller.RefreshHistoryAsync();
            }
        }


        private void rescanAllJournalFilesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Controller.RefreshHistoryAsync(forcejournalreload: true);
        }

        private void checkForNewReleaseToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (CheckForNewinstaller() )
            {
                if (newRelease != null)
                {
                    using (NewReleaseForm frm = new NewReleaseForm(newRelease))
                        frm.ShowDialog(this);
                }
            }
            else
            {
                ExtendedControls.MessageBoxTheme.Show(this,"No new release found", "EDDiscovery", MessageBoxButtons.OK);
            }
        }

        private void deleteDuplicateFSDJumpEntriesToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ExtendedControls.MessageBoxTheme.Show(this, "Confirm you remove any duplicate FSD entries from the current commander", "WARNING", MessageBoxButtons.OKCancel) == DialogResult.OK)
            {
                int n = JournalEntry.RemoveDuplicateFSDEntries(EDCommander.CurrentCmdrID);
                Controller.LogLine("Removed " + n + " FSD entries");
                Controller.RefreshHistoryAsync();
            }
        }

        private void exportVistedStarsListToEliteDangerousToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string exportfilename = null;
            bool found = false;
            string folder = EliteDangerousCore.VisitingStarsCacheFolder.GetVisitedStarsCacheDirectory();

            if (folder != null)
            {
                exportfilename = Path.Combine(folder, "ImportStars.txt");
                found = true;
            }
            else
            {
                SaveFileDialog dlg = new SaveFileDialog();

                dlg.Filter = "ImportedStars export| *.txt";
                dlg.Title = "Could not find VisitedStarsCache.dat file, choose file";
                dlg.FileName = "ImportStars.txt";

                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
                exportfilename = dlg.FileName;
            }

            List<JournalEntry> scans = JournalEntry.GetByEventType(JournalTypeEnum.FSDJump, EDCommander.CurrentCmdrID, new DateTime(2014, 1, 1), DateTime.UtcNow);

            var tscans = scans.ConvertAll<JournalFSDJump>(x => (JournalFSDJump)x);

            try
            {
                using (StreamWriter writer = new StreamWriter(exportfilename, false))
                {
                    foreach (var system in tscans.Select(o => o.StarSystem).Distinct())
                    {
                        writer.WriteLine(system);
                    }
                }

                ExtendedControls.MessageBoxTheme.Show(this, "File " + exportfilename + " created." + Environment.NewLine
                    + (found ? "Restart Elite Dangerous to have this file read into the galaxy map" : ""), "Export visited stars");
            }
            catch (IOException)
            {
                ExtendedControls.MessageBoxTheme.Show(this, "Error writing " + exportfilename, "Export visited stars", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void Open3DMap(HistoryEntry he)
        {
            this.Cursor = Cursors.WaitCursor;

            history.FillInPositionsFSDJumps();

            Map.Prepare(he?.System, EDDConfig.Instance.HomeSystem,
                        EDDConfig.Instance.MapCentreOnSelection ? he?.System : EDDConfig.Instance.HomeSystem,
                        EDDConfig.Instance.MapZoom, Controller.history.FilterByTravel);
            Map.Show();
            this.Cursor = Cursors.Default;
        }

        public void Open3DMapOnSystem(ISystem centerSystem )
        {
            this.Cursor = Cursors.WaitCursor;

            if (centerSystem == null || !centerSystem.HasCoordinate)
                centerSystem = history.GetLastWithPosition.System;

            Map.Prepare(centerSystem, EDDConfig.Instance.HomeSystem, centerSystem,
                             EDDConfig.Instance.MapZoom, history.FilterByTravel);

            Map.Show();
            this.Cursor = Cursors.Default;
        }

        public void Open2DMap()
        {
            this.Cursor = Cursors.WaitCursor;
            Form2DMap frm = new Form2DMap(Controller.history.FilterByFSDAndPosition);
            frm.Show();
            this.Cursor = Cursors.Default;
        }

        private void sendUnsuncedEDDNEventsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<HistoryEntry> hlsyncunsyncedlist = Controller.history.FilterByScanNotEDDNSynced;        // first entry is oldest

            EDDNSync.SendEDDNEvents(LogLine, hlsyncunsyncedlist);
        }

        private void notifyIcon1_DoubleClick(object sender, EventArgs e)
        {
            // Tray icon was double-clicked.
            if (FormWindowState.Minimized == WindowState)
            {
                if (EDDConfig.MinimizeToNotifyIcon)
                    Show();

                if (FormIsMaximised)
                    WindowState = FormWindowState.Maximized;
                else
                    WindowState = FormWindowState.Normal;
            }
            else
                WindowState = FormWindowState.Minimized;
        }

        private void notifyIconMenu_Hide_Click(object sender, EventArgs e)
        {       // horrible circular ref to this sub func then back up.. can't think of a fix for now.
            TabPage t = tabControlMain.GetMajorTab(PanelInformation.PanelIDs.Settings);
            if ( t != null )
                (t.Controls[0] as UserControls.UserControlSettings).DisableNotifyIcon();
        }

        private void notifyIconMenu_Open_Click(object sender, EventArgs e)
        {
            // Tray icon 'Open EDDiscovery' menu item was clicked. Present the main window.
            if (FormWindowState.Minimized == WindowState)
            {
                if (EDDConfig.UseNotifyIcon && EDDConfig.MinimizeToNotifyIcon)
                    Show();

                if (FormIsMaximised)
                    WindowState = FormWindowState.Maximized;
                else
                    WindowState = FormWindowState.Normal;
            }
            else
                Activate();
        }

#endregion

#region "Caption" controls

        private void MouseDownCAPTION(object sender, MouseEventArgs e)
        {
            OnCaptionMouseDown((Control)sender, e);
        }

        private void MouseUpCAPTION(object sender, MouseEventArgs e)
        {
            OnCaptionMouseUp((Control)sender, e);
        }


        private void labelInfoBoxTop_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && newRelease != null)
            {
                using (NewReleaseForm frm = new NewReleaseForm(newRelease))
                    frm.ShowDialog(this);
            }
            else
            {
                MouseDownCAPTION(sender, e);
            }
        }

        private void panel_close_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                Close();
            else
                MouseUpCAPTION(sender, e);
        }

        private void panel_minimize_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
                this.WindowState = FormWindowState.Minimized;
            else
                MouseUpCAPTION(sender, e);
        }

#endregion

#region Updators

        public void NewTargetSet(Object sender)
        {
            if (OnNewTarget != null)
                OnNewTarget(sender);
        }

        public void NoteChanged(Object sender, HistoryEntry snc, bool committed)
        {
            if (OnNoteChanged != null)
                OnNoteChanged(sender, snc,committed);
        }

        public void NewCalculatedRoute(List<ISystem> list)
        {
            if (OnNewCalculatedRoute != null)
                OnNewCalculatedRoute(list);
        }

        public void NewTriLatStars(List<string> list, bool wanted)
        {
            if (OnNewStarsForTrilat != null)
                OnNewStarsForTrilat(list, wanted);
        }

        public void NewExpeditionStars(List<string> list)
        {
            if (OnNewStarsForExpedition != null)
                OnNewStarsForExpedition(list);
        }

#endregion

#region Add Ons
        public void manageAddOnsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            buttonExtManageAddOns_Click(sender,e);
        }

        private void buttonExtManageAddOns_Click(object sender, EventArgs e)
        {
            if (actioncontroller.ManageAddOns())
                OnAddOnsChanged?.Invoke();
        }

        private void configureAddOnActionsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            buttonExtEditAddOns_Click(sender, e);
        }

        private void buttonExtEditAddOns_Click(object sender, EventArgs e)
        {
            if ( actioncontroller.EditAddOns() )
                OnAddOnsChanged?.Invoke();
        }

        private void editLastActionPackToolStripMenuItem_Click(object sender, EventArgs e)
        {
            actioncontroller.EditLastPack();
        }

        private void stopCurrentlyRunningActionProgramToolStripMenuItem_Click(object sender, EventArgs e)
        {
            actioncontroller.TerminateAll();
        }

        public bool AddNewMenuItemToAddOns(string menu, string menutext, string icon , string menuname, string packname)
        {
            ToolStripMenuItem parent;

            menu = menu.ToLower(CultureInfo.InvariantCulture);
            if (menu.Equals("add-ons"))
                parent = addOnsToolStripMenuItem;
            else if (menu.Equals("help"))
                parent = helpToolStripMenuItem;
            else if (menu.Equals("tools"))
                parent = toolsToolStripMenuItem;
            else if (menu.Equals("admin"))
                parent = adminToolStripMenuItem;
            else
                return false;

            Image img = IconSet.GetIcon(icon);

            var x = (from ToolStripItem p in parent.DropDownItems where p.Text.Equals(menutext) && p.Tag != null && p.Name.Equals(menuname) select p);

            if (x.Count() == 0)           // double entries screened out of same menu text from same pack
            {
                ToolStripMenuItem it = new ToolStripMenuItem();
                it.Text = menutext;
                it.Name = menuname;
                it.Tag = packname;
                if (img != null)
                    it.Image = img;
                it.Size = new Size(313, 22);
                it.Click += MenuTrigger_Click;
                parent.DropDownItems.Add(it);
            }

            return true;
        }

        public bool IsMenuItemInstalled(string menuname)
        {
            foreach( ToolStripMenuItem tsi in menuStrip1.Items )
            {
                List<ToolStripItem> presentlist = (from ToolStripItem s in tsi.DropDownItems where s.Name.Equals(menuname) select s).ToList();
                if (presentlist.Count() > 0)
                    return true;
            }

            return false;
        }


        public void RemoveMenuItemsFromAddOns(ToolStripMenuItem menu, string packname)
        {
            List<ToolStripItem> removelist = (from ToolStripItem s in menu.DropDownItems where s.Tag != null && ((string)s.Tag).Equals(packname) select s).ToList();
            foreach (ToolStripItem it in removelist)
            {
                menu.DropDownItems.Remove(it);
                it.Dispose();
            }
        }

        public void RemoveMenuItemsFromAddOns(string packname)
        {
            RemoveMenuItemsFromAddOns(addOnsToolStripMenuItem, packname);
            RemoveMenuItemsFromAddOns(helpToolStripMenuItem, packname);
            RemoveMenuItemsFromAddOns(toolsToolStripMenuItem, packname);
            RemoveMenuItemsFromAddOns(adminToolStripMenuItem, packname);
        }

        private void MenuTrigger_Click(object sender, EventArgs e)
        {
            ToolStripMenuItem it = sender as ToolStripMenuItem;
            Conditions.ConditionVariables vars = new Conditions.ConditionVariables(new string[]
            {   "MenuName", it.Name,
                "MenuText", it.Text,
                "TopLevelMenuName" , it.OwnerItem.Name,
            });

            actioncontroller.ActionRun(Actions.ActionEventEDList.onMenuItem, null, vars);
        }

        public Conditions.ConditionVariables Globals { get { return actioncontroller.Globals; } }

        public int ActionRunOnEntry(HistoryEntry he, ActionLanguage.ActionEvent av)
        { return actioncontroller.ActionRunOnEntry(he, av); }

        public int ActionRun(ActionLanguage.ActionEvent ev, HistoryEntry he = null, Conditions.ConditionVariables additionalvars = null, string flagstart = null, bool now = false)
        { return actioncontroller.ActionRun(ev,he,additionalvars,flagstart,now); }

#endregion

#region Toolbar

        public void LoadCommandersListBox()
        {
            comboBoxCommander.Enabled = false;
            comboBoxCommander.Items.Clear();            // comboBox is nicer with items
            comboBoxCommander.Items.AddRange((from EDCommander c in EDCommander.GetListInclHidden() select c.Name).ToList());
            if (history.CommanderId == -1)
            {
                comboBoxCommander.SelectedIndex = 0;
                buttonExtEDSMSync.Enabled = false;
            }
            else
            {
                comboBoxCommander.SelectedItem = EDCommander.Current.Name;
                buttonExtEDSMSync.Enabled = EDCommander.Current.SyncToEdsm | EDCommander.Current.SyncFromEdsm;
            }

            comboBoxCommander.Enabled = true;
        }

        private void comboBoxCommander_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxCommander.SelectedIndex >= 0 && comboBoxCommander.Enabled)     // DONT trigger during LoadCommandersListBox
            {
                var itm = (from EDCommander c in EDCommander.GetListInclHidden() where c.Name.Equals(comboBoxCommander.Text) select c).ToList();

                EDCommander.CurrentCmdrID = itm[0].Nr;
                Controller.RefreshHistoryAsync(currentcmdr: EDCommander.CurrentCmdrID);                                   // which will cause DIsplay to be called as some point
            }

        }

        private void buttonExt3dmap_Click(object sender, EventArgs e)
        {
            Open3DMap(PrimaryCursor.GetCurrentHistoryEntry);
        }

        private void buttonExt2dmap_Click(object sender, EventArgs e)
        {
            Open2DMap();
        }

        public void RefreshButton(bool state)
        {
            buttonExtRefresh.Enabled = state;
        }

        private void buttonExtRefresh_Click(object sender, EventArgs e)
        {
            LogLine("Refresh History.");
            RefreshHistoryAsync();
        }

        private void buttonExtEDSMSync_Click(object sender, EventArgs e)
        {
            EDSMClass edsm = new EDSMClass();

            if (!edsm.ValidCredentials)
            {
                ExtendedControls.MessageBoxTheme.Show(this, "Please ensure a commander is selected and it has a EDSM API key set");
                return;
            }

            try
            {
                EDSMJournalSync.SendEDSMEvents(l => LogLine(l), history, manual: true);
            }
            catch (Exception ex)
            {
                LogLine($"EDSM Sync failed: {ex.Message}");
            }

        }

#endregion

#region PopOuts

        ExtendedControls.DropDownCustom popoutdropdown;

        private void buttonExtPopOut_Click(object sender, EventArgs e)
        {
            popoutdropdown = new ExtendedControls.DropDownCustom("", true);

            popoutdropdown.ItemHeight = 26;
            popoutdropdown.Items = PanelInformation.GetPanelDescriptions().ToList();
            popoutdropdown.ImageItems = PanelInformation.GetPanelImages().ToList();
            PanelInformation.PanelIDs[] pids = PanelInformation.GetPanelIDs();
            popoutdropdown.FlatStyle = FlatStyle.Popup;
            popoutdropdown.Activated += (s, ea) =>
            {
                Point location = buttonExtPopOut.PointToScreen(new Point(0, 0));
                popoutdropdown.Location = popoutdropdown.PositionWithinScreen(location.X + buttonExtPopOut.Width, location.Y);
                this.Invalidate(true);
            };
            popoutdropdown.SelectedIndexChanged += (s, ea) =>
            {
                PopOuts.PopOut(pids[popoutdropdown.SelectedIndex]);
            };

            popoutdropdown.Size = new Size(500,600);
            theme.ApplyToControls(popoutdropdown);
            popoutdropdown.SelectionBackColor = theme.ButtonBackColor;
            popoutdropdown.Show(this);
        }

        internal void SaveCurrentPopOuts()
        {
            PopOuts.SaveCurrentPopouts();
        }

        internal void LoadSavedPopouts()
        {
            PopOuts.LoadSavedPopouts();
        }

#endregion

#region Misc

        public void SetUpLogging()      // controls logging of HTTP stuff
        {
            BaseUtils.HttpCom.LogPath = EDDConfig.Instance.EDSMLog ? EDDOptions.Instance.AppDataDirectory : null;
        }

        #endregion

    }
}



