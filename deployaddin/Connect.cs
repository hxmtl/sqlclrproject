using System;
using Extensibility;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.CommandBars;
using System.Resources;
using System.Reflection;
using System.Globalization;
using System.Windows.Forms;
using System.Collections;
using System.IO;
using Microsoft.Build.BuildEngine;
using System.Diagnostics;
using System.Text;
using System.Xml;
using System.Xml.XPath;
using System.Data;
using System.Data.SqlClient;



namespace DeployAddIn {
	/// <summary>The object for implementing an Add-in.</summary>
	/// <seealso class='IDTExtensibility2' />
	public class Connect : IDTExtensibility2, IDTCommandTarget {

    #region fields
    private DTE2 _app;
    private AddIn _addIn;
    Commands2 commands = null;
    bool isToolWin = false;
    Window2 toolWin = null;
    DepProp propForm = null;
    Hashtable cmdHt;
    OutputWindowPane owp = null;
    OutputWindow ow = null;
    EnvDTE.SolutionEvents solevt = null;
    string _connString = "";
    EnvDTE80.Process2 proc = null;
    string projPath = "";
    Control depCtrl;

    #endregion

    public Connect() {
      

		}

    string ProjectPath {
      get {
        if (projPath == string.Empty) {
          Array projs = (Array)_app.ActiveSolutionProjects;
          if (projs.Length > 0) {
            EnvDTE.Project pr = (EnvDTE.Project)projs.GetValue(0);
            projPath = Path.GetDirectoryName(pr.FullName);
          }
        }
        return projPath;
      }
    }

    string ConnectionString {
      get {

        string sqlFile = Path.Combine(ProjectPath, "sql.proj");
        if (!File.Exists(sqlFile))
        {
          MessageBox.Show("The deployment project file 'sql.proj' can not be found. Please open the property form; choose 'Deployment Properties' from the 'Tools | SQL CLR Deployment' menu.", "Property File Not Found");
        }
        else
        {
          XPathDocument doc = new XPathDocument(sqlFile);
          XPathNavigator nav = doc.CreateNavigator();

          XmlNamespaceManager manager = new XmlNamespaceManager(nav.NameTable);
          manager.AddNamespace("p", "http://schemas.microsoft.com/developer/msbuild/2003");
          XPathNodeIterator propNodes = nav.Select("/p:Project/p:PropertyGroup/p:Connectionstring", manager);
          propNodes.MoveNext();
          XPathNavigator nav2 = propNodes.Current;
          _connString = nav2.Value;
        }


        return _connString;
      }

    }


    public void OnConnection(object application, ext_ConnectMode connectMode, object addInInst, ref Array custom) {


      if (connectMode == ext_ConnectMode.ext_cm_Startup) {

        _app = (DTE2)application;
        _addIn = (AddIn)addInInst;
        CommandBarPopup depMainPop = null;
        cmdHt = new Hashtable();
        commands = (Commands2)_app.Commands;

        solevt = _app.Events.SolutionEvents;
        solevt.AfterClosing += new _dispSolutionEvents_AfterClosingEventHandler(solevt_AfterClosing);
        solevt.ProjectAdded += new _dispSolutionEvents_ProjectAddedEventHandler(solevt_ProjectAdded);
        solevt.Opened += new _dispSolutionEvents_OpenedEventHandler(solevt_Opened);
        Hashtable ht = new Hashtable();
        foreach (Command c in commands) {
          if (c.Name.Contains("DeployAddIn"))
            cmdHt.Add(c.Name, c);
        }

        object[] contextGUIDS = new object[] { };
        string toolsMenuName;

        try {

          ResourceManager resourceManager = new ResourceManager("DeployAddIn.CommandBar", Assembly.GetExecutingAssembly());
          CultureInfo cultureInfo = new System.Globalization.CultureInfo(_app.LocaleID);
          string resourceName = String.Concat(cultureInfo.TwoLetterISOLanguageName, "Tools");
          toolsMenuName = resourceManager.GetString(resourceName);
        }
        catch {
          //We tried to find a localized version of the word Tools, but one was not found.
          //  Default to the en-US word, which may work for the current culture.
          toolsMenuName = "Tools";
        }

        //Place the command on the tools menu.
        //Find the MenuBar command bar, which is the top-level command bar holding all the main menu items:
        Microsoft.VisualStudio.CommandBars.CommandBar menuBar = ((Microsoft.VisualStudio.CommandBars.CommandBars)_app.CommandBars)["MenuBar"];

        //Find the Tools command bar on the MenuBar command bar:
        CommandBarControl toolsControl = menuBar.Controls[toolsMenuName];

        CommandBarPopup toolsPopup = (CommandBarPopup)toolsControl;
        try {
          depMainPop = (CommandBarPopup)toolsPopup.Controls.Add(vsCommandBarType.vsCommandBarTypePopup, 1, "", 1, true);
          depMainPop.Caption = "SQL CLR Deployment";
          depMainPop.BeginGroup = true;
          depMainPop.Visible = true;

          

          //Add a command to the Commands collection:
          AddCmd(depMainPop.CommandBar, "DepProp", "Deployment Properties", "Allows you to set properties for the deployment task.", 1);
        }


        catch (Exception ex1) {
          MessageBox.Show(ex1.Message, "Error");

        }


        try {

          CommandBarPopup depSubPop = (CommandBarPopup)depMainPop.Controls.Add(vsCommandBarType.vsCommandBarTypePopup, 1, "", 2, true);
          depSubPop.Caption = "Deploy/Drop Assemblies";
          depSubPop.BeginGroup = true;
          depSubPop.Visible = true;

          
          

          AddCmd(depSubPop.CommandBar, "DepAll", "Deploy All (assembly, methods etc)", "Deploys the assembly and all the procedures, functions etc.", 1);
          AddCmd(depSubPop.CommandBar, "DepAsm", "Deploy Assembly", "Deploys the assembly.", 2);
          AddCmd(depSubPop.CommandBar, "DepUdt", "Create UDT's", "Creates the user defined types in the assembly.", 3);
          AddCmd(depSubPop.CommandBar, "DepMeth", "Create Methods", "Creates the procedures, functions etc, in the assembly", 4);
          AddCmd(depSubPop.CommandBar, "DropAll", "Drop All (assembly, methods etc)", "Drops the assembly and all the procedures, functions etc.", 5);
          AddCmd(depMainPop.CommandBar, "Debug", "Debug", "Debug the assembly.", 3);

        }
        catch (Exception ex2) {
          MessageBox.Show(ex2.Message, "Error");

        }

        
      }
    }

    void depCtrl_Click(object sender, EventArgs e) {
      MessageBox.Show("Deploy Control Clicked");
    }

    void solevt_Opened() {
      ResetDepProp();
      
    }

    void solevt_ProjectAdded(EnvDTE.Project Project) {
      ResetDepProp();
    }

    void solevt_AfterClosing() {

      ResetDepProp();
        

      
    }

    private void ResetDepProp() {
      if (isToolWin) {
        propForm.Hide();
        propForm = null;
        toolWin.Close(vsSaveChanges.vsSaveChangesNo);
        isToolWin = false;
        propForm.FirstLoad = true;

      }

      _connString = "";
      projPath = "";
      proc = null;
    }

    private object[] AddCmd(CommandBar parent, string name, string caption, string decription, int pos) {
      object[] contextGUIDS = new object[] { };
      string cmdName = string.Format("{0}.{1}", this.GetType().ToString(), name);
      Command cmd = null;
      //myCommand = applicationObject.Commands.Item(addInInstance.ProgID & "." & "MyCommand");
      //Add a command to the Commands collection:
      try {
        //look for the command in the hashtable
        cmd = (Command)cmdHt[cmdName];
        //cmd = commands.Item(cmdName, (int)System.Type.Missing);
        
        
        if (cmd == null) {
          cmd = commands.AddNamedCommand2(_addIn, name, caption, decription, true, 59, ref contextGUIDS, (int)vsCommandStatus.vsCommandStatusSupported + (int)vsCommandStatus.vsCommandStatusEnabled, (int)vsCommandStyle.vsCommandStyleText, vsCommandControlType.vsCommandControlTypeButton);
          cmdHt.Add(cmd.Name, cmd);
        }
        
        }

      catch (Exception e) {
        MessageBox.Show(e.Message, "Error");
      }

      //Add a control for the command to the menu:
      if ((cmd != null) && (parent != null)) {
       cmd.AddControl(parent, pos);
      }
      else
        MessageBox.Show("Command or parent == null");
      
      return contextGUIDS;
    }

		/// <summary>Implements the OnDisconnection method of the IDTExtensibility2 interface. Receives notification that the Add-in is being unloaded.</summary>
		/// <param term='disconnectMode'>Describes how the Add-in is being unloaded.</param>
		/// <param term='custom'>Array of parameters that are host application specific.</param>
		/// <seealso class='IDTExtensibility2' />
		public void OnDisconnection(ext_DisconnectMode disconnectMode, ref Array custom) {
      if (isToolWin) {
        //propForm.Dispose();
        //toolWin.Close(vsSaveChanges.vsSaveChangesNo);
        toolWin.Visible = false;
      }
      
		}

		/// <summary>Implements the OnAddInsUpdate method of the IDTExtensibility2 interface. Receives notification when the collection of Add-ins has changed.</summary>
		/// <param term='custom'>Array of parameters that are host application specific.</param>
		/// <seealso class='IDTExtensibility2' />		
		public void OnAddInsUpdate(ref Array custom)
		{
		}

		/// <summary>Implements the OnStartupComplete method of the IDTExtensibility2 interface. Receives notification that the host application has completed loading.</summary>
		/// <param term='custom'>Array of parameters that are host application specific.</param>
		/// <seealso class='IDTExtensibility2' />
		public void OnStartupComplete(ref Array custom)
		{
		}

		/// <summary>Implements the OnBeginShutdown method of the IDTExtensibility2 interface. Receives notification that the host application is being unloaded.</summary>
		/// <param term='custom'>Array of parameters that are host application specific.</param>
		/// <seealso class='IDTExtensibility2' />
		public void OnBeginShutdown(ref Array custom)
		{
		}
		
		/// <summary>Implements the QueryStatus method of the IDTCommandTarget interface. This is called when the command's availability is updated</summary>
		/// <param term='commandName'>The name of the command to determine state for.</param>
		/// <param term='neededText'>Text that is needed for the command.</param>
		/// <param term='status'>The state of the command in the user interface.</param>
		/// <param term='commandText'>Text requested by the neededText parameter.</param>
		/// <seealso class='Exec' />
		public void QueryStatus(string commandName, vsCommandStatusTextWanted neededText, ref vsCommandStatus status, ref object commandText)
		{
			if(neededText == vsCommandStatusTextWanted.vsCommandStatusTextWantedNone)
			{
				if(commandName == "DeployAddIn.Connect.DepProp")
				{
					status = (vsCommandStatus)vsCommandStatus.vsCommandStatusSupported|vsCommandStatus.vsCommandStatusEnabled;
					return;
				}

        if (commandName == "DeployAddIn.Connect.DepAll") {
          status = (vsCommandStatus)vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
          return;
        }

        if (commandName == "DeployAddIn.Connect.DepAsm") {
          status = (vsCommandStatus)vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
          return;
        }

        if (commandName == "DeployAddIn.Connect.DepUdt") {
          status = (vsCommandStatus)vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
          return;
        }

        if (commandName == "DeployAddIn.Connect.DepMeth") {
          status = (vsCommandStatus)vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
          return;
        }

        if (commandName == "DeployAddIn.Connect.DropAll") {
          status = (vsCommandStatus)vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
          return;
        }
        if (commandName == "DeployAddIn.Connect.Debug") {
          status = (vsCommandStatus)vsCommandStatus.vsCommandStatusSupported | vsCommandStatus.vsCommandStatusEnabled;
          return;
        }
			}
		}

		/// <summary>Implements the Exec method of the IDTCommandTarget interface. This is called when the command is invoked.</summary>
		/// <param term='commandName'>The name of the command to execute.</param>
		/// <param term='executeOption'>Describes how the command should be run.</param>
		/// <param term='varIn'>Parameters passed from the caller to the command handler.</param>
		/// <param term='varOut'>Parameters passed from the command handler to the caller.</param>
		/// <param term='handled'>Informs the caller if the command was handled or not.</param>
		/// <seealso class='Exec' />
		public void Exec(string commandName, vsCommandExecOption executeOption, ref object varIn, ref object varOut, ref bool handled)
		{
			handled = false;
      bool doDep = false;
      bool asmDep = false;
      string target = "";
			if(executeOption == vsCommandExecOption.vsCommandExecOptionDoDefault) {

				if(commandName == "DeployAddIn.Connect.DepProp") {
          ShowDeploymentProperties();
          handled = true;
					return;
				}
        else {

          
          if (commandName == "DeployAddIn.Connect.DepAll") {
            doDep = true;
            target = "DeployAll";
            asmDep = true;
            
          }

          else if (commandName == "DeployAddIn.Connect.DepAsm") {
            doDep = true;
            target = "DeployAsm";
            asmDep = true;
          }

          else if (commandName == "DeployAddIn.Connect.DepUdt") {
            doDep = true;
            target = "DeployUdt";
          }

          else if (commandName == "DeployAddIn.Connect.DepMeth") {
            doDep = true;
            target = "DeployMeth";
          }

          else if (commandName == "DeployAddIn.Connect.DropAll") {
            doDep = true;
            target = "DropAssembly";
          }

          else if (commandName == "DeployAddIn.Connect.Debug") {
            RunDebug();
          }

          if (doDep) {
            //check that we have a connection string
            //System.Diagnostics.Debugger.Launch();
            bool isLocal;
            bool res = true;
            bool validConn = false;
            if (propForm != null && propForm.IsDirty)
              res = propForm.UpdateData();

            if(res)
              validConn = ValidateConnection(out isLocal);

            
            
            if (res && validConn) {
              res = DoDeploy(target);
            }
            else if(!res && validConn)
              MessageBox.Show("The property page could not be updated. Correct the errors before trying to deploy", "Property page error");

          }

          handled = true;
          return;
        }
			}
		}

    void RunDebug() {
      bool isLocal;
      StringBuilder sb = new StringBuilder();

      //Guid gSql = new Guid("{1202F5B4-3522-4149-BAD8-58B2079D704F}");
      //Guid gCLR = new Guid("{449EC4CC-30D2-4032-9256-EE18EB41B62B}");

      //get the connection-string and check whether we're doing local
      //or remote debugging
      bool ret = ValidateConnection(out isLocal);
      try {
        if (ret) {
          Document doc = _app.ActiveDocument;
          if (doc.Name.Contains("test_")) {
            string cmdText = ((TextSelection)doc.Selection).Text;
            if (cmdText != string.Empty) {
              CreateOutputWindow("Debug");
              ret = AttachProc(isLocal);
              //ret = true;
              if (ret) {
                ExecStmtDel ed = new ExecStmtDel(ExecStmt);
                IAsyncResult iar = ed.BeginInvoke(cmdText, new AsyncCallback(DoneExec), ed);
              }
            }
            else {
              MessageBox.Show("No statement selected to executes.", "No Debug");
            }
          }
          else {
            MessageBox.Show("You need to select a statement from one of the 'test_*.sql documents.", "No Debug");
          }
        }

      }

      finally {
        //if (proc != null) {
        //  if (ret)
        //    proc.Detach(false);

        //  proc = null; ;

        //}
      }
    
    }

    void DoneExec(IAsyncResult iar) {
      try {
        ExecStmtDel ed = (ExecStmtDel)iar.AsyncState;
        bool ret = ed.EndInvoke(iar);
      }
      finally {

        if (proc != null) {
          proc.Detach(false);
          proc = null; ;
        }
      }

    }
     

    private bool AttachProc(bool isLocal) {
      bool ret = false;
      EnvDTE80.Debugger2 dbg = (Debugger2)_app.Debugger;
      //get the active doc
      //make sure it is one of our test documents

      //attach to the process
      foreach (EnvDTE80.Process2 p in dbg.LocalProcesses) {
        if (p.Name.Contains("sqlservr.exe")) {
          proc = p;
          //attach to the managed code engine
          proc.Attach2("{449EC4CC-30D2-4032-9256-EE18EB41B62B}");
          //p.Attach2("{1202F5B4-3522-4149-BAD8-58B2079D704F}");
          owp.OutputString(string.Format("Attached to the SQL Server process, process Id: {0}", p.ProcessID.ToString()));
          //MessageBox.Show(string.Format("Attached to the SQL Server process, process Id: {0}", p.ProcessID.ToString()));
          ret = true;
          break;
        }

      }
      return ret;

    }

    //one of the test methods for debugging, running against
    //sqlcmd - due to bug in S2K5, where we hang wen executing
    //this method causes hang as well
    bool ExecStmt2(string cmdText) {

      OutputWindow ow = _app.ToolWindows.OutputWindow;

      ow.Parent.Activate();
      try {
        owp = ow.OutputWindowPanes.Item("Debug");
      }

      catch {
        owp = ow.OutputWindowPanes.Add("Debug");
      }

      try {
        _app.StatusBar.Text = "Debug started";
        ow.Parent.Activate();
        owp.Activate();
        string buildPath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        buildPath = Path.Combine(buildPath, "MSBUILD.exe");

        owp.OutputString("Debug Started\n");
        System.Diagnostics.Process p = new System.Diagnostics.Process();
        string sqlFile = "";

        //sqlcmd -d test2 -Q "select dbo.MyAdder99



        sqlFile = Path.Combine(ProjectPath, "sql.proj");
        //string paramString = string.Format("\"{0}\" /t:ExecDebug /p:CmdText=\"{1}\"", sqlFile, cmdText);
        //p.StartInfo = new ProcessStartInfo("cmd.exe", "/k " + buildPath + " \"" + sqlFile + "\" /t:ExecDebug /p:CmdText=\"" + cmdText + "\"");
        p.StartInfo = new ProcessStartInfo("sqlcmd.exe", "-d test2 -Q \"" + cmdText + "\"");

        p.StartInfo.UseShellExecute = false;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.RedirectStandardOutput = true;
        p.OutputDataReceived += new DataReceivedEventHandler(p_OutputDataReceived);
        p.Start();

        p.BeginOutputReadLine();
        //owp.OutputString(p.StandardOutput.ReadToEnd());
        p.WaitForExit();

        owp.Activate();
        string statusMsg = "succeeded";
        if (p.ExitCode != 0)
          statusMsg = "failed";

        _app.StatusBar.Text = string.Format("Debug {0}", statusMsg);
        ow.Parent.Activate();
        //ow.Parent.AutoHides = true;
        return true;

      }

      catch (Exception e) {
        _app.StatusBar.Text = "Debug failed";

        string msg = string.Format("An unexpected exception occured.\n\nThe exception is: {0}", e.Message);
        owp.OutputString(msg);
        return false;

      }

      finally {

      }

    }

    //one of the test methods for debugging, running against
    //sqlcmd - due to bug in S2K5, where we hang wen executing
    //this method causes hang as well
    bool ExecStmt(string cmdText) {

      if(ow != null)
        CreateOutputWindow("Debug");

      try {
        _app.StatusBar.Text = "Debug started";
        string buildPath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        buildPath = Path.Combine(buildPath, "MSBUILD.exe");

        owp.OutputString("Debug Started\n");
        System.Diagnostics.Process p = new System.Diagnostics.Process();
        string sqlFile = "";

        sqlFile = Path.Combine(ProjectPath, "sql.proj");
        string paramString = string.Format("\"{0}\" /t:ExecDebug /p:CmdText=\"{1}\"", sqlFile, cmdText);
        p.StartInfo = new ProcessStartInfo(buildPath, paramString);
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.RedirectStandardOutput = true;
        p.OutputDataReceived += new DataReceivedEventHandler(p_OutputDataReceived);
        p.Start();
        p.BeginOutputReadLine();
       
        p.WaitForExit();

        owp.Activate();
        string statusMsg = "succeeded";
        if (p.ExitCode != 0)
          statusMsg = "failed";

        _app.StatusBar.Text = string.Format("Debug {0}", statusMsg);
        ow.Parent.Activate();
        ow.Parent.AutoHides = true;
        return true;

      }

      catch (Exception e) {
        _app.StatusBar.Text = "Debug failed";

        string msg = string.Format("An unexpected exception occured.\n\nThe exception is: {0}", e.Message);
        owp.OutputString(msg);
        return false;

      }

      finally {

      }

    }

    private void CreateOutputWindow(string winText) {
      ow = _app.ToolWindows.OutputWindow;

      ow.Parent.Activate();
      try {
        owp = ow.OutputWindowPanes.Item(winText);
        owp.Clear();
        ow.Parent.Activate();
        owp.Activate();
      }

      catch {
        owp = ow.OutputWindowPanes.Add(winText);
      }
    }

    bool ValidateConnection(out bool isLocal) {
      bool _isLocal = true;
      bool ret = false;
      bool isDone = false;
      string[] connAr = ConnectionString.Split(';');
      string machineName = "";
      string dbName = "";
      foreach (string sConn in connAr) {
        if (sConn.ToUpper().Contains("SERVER")) {
          string[] servAr = sConn.Split('=');
          machineName = servAr[1].Replace(" ", "");
          _isLocal = machineName.ToUpper().Contains("LOCALHOST") || machineName.ToUpper().Contains(System.Environment.MachineName.ToUpper());
          if (machineName == string.Empty) {
            MessageBox.Show("The server name is not valid.", "No Debug");
            _connString = "";
            isLocal = true;
            return false;
          }
          if (isDone) {
            ret = true;
            break;
          }
          isDone = true;
        }

        if (sConn.ToUpper().Contains("DATABASE")) {
          string[] servAr = sConn.Split('=');
          dbName = servAr[1].Replace(" ", "");
          if (dbName.ToUpper().Contains("[DB_NAME]") || dbName == string.Empty) {
            MessageBox.Show("The database name is not valid.", "No Debug");
            _connString = "";
            isLocal = true;
            return false;
          }
          else {
            if (isDone) {
              ret = true;
              break;
            }
            isDone = true;
          }

        }

      }
      isLocal = _isLocal;
      return ret;
    }
    
    bool DoDeploy(string target) {
      Array projs = (Array)_app.ActiveSolutionProjects;
      

      if (ProjectPath == string.Empty) {
        MessageBox.Show("It looks like there is no deployment property file (sql.proj) available. A property form will now be presented for you, where you can set the various deployment properties before you deploy.", "No Properties Set");
        ShowDeploymentProperties();
        return false;
      }

      CreateOutputWindow(target);
      


      try {
        _app.StatusBar.Text = "Deployment/Drop started";
        ow.Parent.Activate();
        owp.Activate();
        string sqlFile = "";
        sqlFile = Path.Combine(ProjectPath, "sql.proj");
        Utility.VerifySqlProjFile(sqlFile);
        string buildPath = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
        buildPath = Path.Combine(buildPath, "MSBUILD.exe");
        
        owp.OutputString("Deployment Started\n");
        System.Diagnostics.Process p = new System.Diagnostics.Process();
        
        string testSqlFile = "\"" + sqlFile + "\"";
        
        p.StartInfo = new ProcessStartInfo(buildPath, testSqlFile + " /t:" + target);
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.CreateNoWindow = true;
        p.StartInfo.RedirectStandardOutput = true;
        p.OutputDataReceived += new DataReceivedEventHandler(p_OutputDataReceived);
        p.Start();
        p.BeginOutputReadLine();
        p.WaitForExit();
        
        owp.Activate();
        string statusMsg = "succeeded";
        if (p.ExitCode != 0)
          statusMsg = "failed";

        _app.StatusBar.Text = string.Format("Deployment/Drop {0}", statusMsg);
        ow.Parent.Activate();
        
        return true;

      }

      catch (Exception e) {
        _app.StatusBar.Text = "Deployment failed";
        
        string msg = string.Format("An unexpected exception occured.\n\nThe exception is: {0}", e.Message);
        owp.OutputString(msg);
        return false;

      }

      finally {
        
        
          
      }

    }

    void p_OutputDataReceived(object sender, DataReceivedEventArgs e) {
          
      if (!String.IsNullOrEmpty(e.Data))
        owp.OutputString(e.Data + Environment.NewLine);
              
    }

    private void ShowDeploymentProperties() {
      string guidStr = "{426E8D27-3D33-4fc8-B3E9-9883AADC679F}";
      object objTemp = null;

      
      string projPath = ProjectPath;
      string execAsm = Assembly.GetExecutingAssembly().Location;
          

      Windows2 wins = (Windows2)_app.Windows;

      try {
        if (!isToolWin) {

          toolWin = (Window2)wins.CreateToolWindow2(_addIn, execAsm, "DeployAddIn.DepProp", "Deployment Properties", guidStr, ref objTemp);
          toolWin.IsFloating = false;
          toolWin.Linkable = false;
          propForm = (DepProp)objTemp;
          propForm.AutoScroll = true;
          //propForm.Init(projPath);
          isToolWin = true;
        }

        propForm.Init(ProjectPath);
        if (wins == null)
          toolWin.Visible = true;

        //toolWin.Activate();
        toolWin.Visible = true;

        
        
      }
      catch (Exception e) {
        MessageBox.Show(e.Message);
      }
    }



    delegate bool ExecStmtDel(string cmd);
    
  }

}