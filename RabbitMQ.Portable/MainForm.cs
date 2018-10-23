using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace RabbitMQ.Portable
{
    public partial class MainForm : Form
    {
        private readonly string _homeDirectory;
        private readonly string _sysHomeDirectory;
        private readonly string _sysHomeDrive;
        private string _dataDirectory;
        private string _erlangDirectory;
        private string _ertsDirectory;
        private Process _process;
        private string _rmqDirectory;

        /// <inheritdoc />
        /// <summary>
        ///     Default constructor
        /// </summary>
        public MainForm()
        {
            InitializeComponent();

            // Find a location of RabbitMqPortable.exe
            var uri = new UriBuilder(Assembly.GetExecutingAssembly().CodeBase);
            _homeDirectory = Path.GetDirectoryName(Uri.UnescapeDataString(uri.Path));
            _sysHomeDrive = _homeDirectory != null && _homeDirectory[1] == ':' ? _homeDirectory.Substring(0, 2) : "C:";
            _sysHomeDirectory = _homeDirectory != null && _homeDirectory[1] == ':'
                ? _homeDirectory.Substring(2) + @"\data\"
                : "\\";
        }

        /// <summary>
        ///     On form load ...
        ///     Prepare all settings and variables for successfully
        ///     server start (and stop) and check if everything is fine
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_Load(object sender, EventArgs e)
        {
            //********************************************************************
            // OK.. Now let's check which directories we have there ... 
            var dirs = Directory.GetDirectories(_homeDirectory);
            foreach (var d in dirs)
            {
                var rp = d.Replace(_homeDirectory, "").Replace("\\", ""); // Get just a directory name
                if (rp.ToLower().StartsWith("erl")) // if this is erlang directory.. 
                {
                    _erlangDirectory = d; // OK.. This save this as base erlang directory.. 
                    var edirs = Directory.GetDirectories(_erlangDirectory); // Now search inside.. 
                    foreach (var ed in edirs)
                    {
                        var erp = ed.Replace(_erlangDirectory, "").Replace("\\", "");
                        if (erp.StartsWith("erts")) // And if this is erts ...
                            _ertsDirectory = Path.Combine(_erlangDirectory, erp + "\\bin"); // Save it.. 
                    }

                    tsErlang.Text = rp;
                }
                else if (rp.ToLower().StartsWith("rabbit")) // if this is rabbit-mq directory.. 
                {
                    _rmqDirectory = d; // save it.. 
                    tsRabbitMQ.Text = rp;
                }
                else if (rp.ToLower().Equals("data"))
                {
                    _dataDirectory = d; // Save it.. 
                }
            }

            //********************************************************************
            // OK.. Now let's check if we find everything we need .... 
            if (string.IsNullOrEmpty(_erlangDirectory))
            {
                MessageBox.Show(@"Cant find erlang directory", Text, MessageBoxButtons.OK, MessageBoxIcon.Stop);
                Close();
                return;
            }

            if (string.IsNullOrEmpty(_ertsDirectory))
            {
                MessageBox.Show(
                    @"Cant find erts directory inside erlang directory. Look like you have invalid or uncomplete erlang directory",
                    Text, MessageBoxButtons.OK, MessageBoxIcon.Stop);
                Close();
                return;
            }

            if (string.IsNullOrEmpty(_rmqDirectory))
            {
                MessageBox.Show(@"Cant find rabbitmq directory.", Text, MessageBoxButtons.OK, MessageBoxIcon.Stop);
                Close();
                return;
            }


            if (string.IsNullOrEmpty(_dataDirectory))
            {
                _dataDirectory = Path.Combine(_homeDirectory, "data");
                try
                {
                    Directory.CreateDirectory(_dataDirectory);
                }
                catch
                {
                    MessageBox.Show(@"Cant create '" + _dataDirectory + @"' directory.", Text, MessageBoxButtons.OK,
                        MessageBoxIcon.Stop);
                    Close();
                    return;
                }
            }

            //*********************************************************************************
            //  OK.. Now update erl.ini with actual paths
            var iniFile = Path.Combine(_erlangDirectory, "bin\\erl.ini");
            try
            {
                var iniContent = "[erlang]\nBindir={0}\nProgname=erl\nRootdir={1}\n";
                var nc = string.Format(iniContent, _ertsDirectory.Replace(@"\", @"\\"),
                    _erlangDirectory.Replace(@"\", @"\\"));

                File.WriteAllText(iniFile, nc);
            }
            catch (Exception ex)
            {
                MessageBox.Show(@"Error updating " + iniFile + @"\n\n" + ex.Message, Text, MessageBoxButtons.OK,
                    MessageBoxIcon.Stop);
                Close();
                return;
            }

            //***************************************************************************
            // Prepare bat file for 'start console' ... 
            var batStart = "@setlocal\n";
            batStart += "@set ERLANG_HOME=" + _erlangDirectory + "\\\n";
            batStart += "@set RABBITMQ_BASE=" + _homeDirectory + "\\data\\\n";
            batStart += "@set RABBITMQ_CONFIG_FILE=" + _homeDirectory + "\\data\\config\n";
            batStart += "@set RABBITMQ_ADVANCED_CONFIG_FILE=" + _homeDirectory + "\\data\\config\n";
            batStart += "@set RABBITMQ_LOG_BASE=" + _homeDirectory + "\\data\\log\n";
            batStart += "@set LOGS=\n";

            batStart += "@set HOMEDRIVE=" + _sysHomeDrive + "\n";
            batStart += "@set HOMEPATH=" + _sysHomeDirectory + "\n";
            batStart += "@cmd.exe";
            File.WriteAllText(Path.Combine(_rmqDirectory, @"sbin\startShell.bat"), batStart);

            WriteLineToOutput("", Color.White); // Add one empty line....

            if (!StartServer()) // And finnally try to start server ...
                Close();
        }

        /// <summary>
        ///     Capture main process stderr output
        ///     and display it on main window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            try
            {
                if (e.Data != null)
                {
                    Debug.WriteLine(e.Data);
                    Invoke(new MethodInvoker(delegate { WriteLineToOutput(e.Data, Color.Red); }));
                }
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        ///     Capture main process stdout output
        ///     and display it on main window
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Process_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            try
            {
                if (e.Data != null)
                {
                    Debug.WriteLine(e.Data);
                    Invoke(new MethodInvoker(delegate { WriteLineToOutput(e.Data, Color.LimeGreen); }));
                }
            }
            catch (Exception ex)
            {
            }
        }

        /// <summary>
        ///     On form closing...
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (MessageBox.Show(@"Are you sure you want to exit?", @"Confirm exit", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question) == DialogResult.No) e.Cancel = true;

            StopServer();
        }

        /// <summary>
        ///     On 'Start console' menu item
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartConsoleToolStripMenuItem_Click(object sender, EventArgs e)
        {
            var console = new Process
            {
                StartInfo =
                {
                    UseShellExecute = false,
                    FileName = Path.Combine(_rmqDirectory, @"sbin\startShell.bat"),
                    WorkingDirectory = Path.Combine(_rmqDirectory, "sbin")
                }
            };
            console.Start();
        }

        /// <summary>
        ///     On 'Start server' menu item
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StartServerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StartServer();
        }

        /// <summary>
        ///     On 'Stop console' menu item
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StopServerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StopServer();
        }

        /// <summary>
        ///     Start RabbitMQ server in portable mode
        /// </summary>
        /// <returns>True if successfully</returns>
        private bool StartServer()
        {
            //*********************************************************************************
            //  OK.. Now prepare process with environment vars that run RabbitMQ server 
            //  and kick it
            try
            {
                _process = new Process();
                _process.StartInfo.EnvironmentVariables["ERLANG_HOME"] = _erlangDirectory + @"\"; // Where is erlang ? 
                _process.StartInfo.EnvironmentVariables["RABBITMQ_BASE"] =
                    _homeDirectory + @"\data\"; // Where to put RabbitMQ logs and database
                _process.StartInfo.EnvironmentVariables["RABBITMQ_CONFIG_FILE"] =
                    _homeDirectory + @"\data\config"; // Where is config file
                _process.StartInfo.EnvironmentVariables["RABBITMQ_ADVANCED_CONFIG_FILE"] =
                    _homeDirectory + @"\data\config"; // Where is config file
                _process.StartInfo.EnvironmentVariables["RABBITMQ_LOG_BASE"] =
                    _homeDirectory + @"\data\log"; // Where are log files
                _process.StartInfo.EnvironmentVariables.Remove("LOGS");

                _process.StartInfo.EnvironmentVariables["HOMEDRIVE"] =
                    _sysHomeDrive; // Erlang need this for cookie file
                _process.StartInfo.EnvironmentVariables["HOMEPATH"] =
                    _sysHomeDirectory; // Erlang need this for cookie file
                _process.StartInfo.UseShellExecute = false;
                _process.StartInfo.CreateNoWindow = true;
                _process.StartInfo.RedirectStandardOutput = true;
                _process.StartInfo.RedirectStandardError = true;
                _process.OutputDataReceived += Process_OutputDataReceived;
                _process.ErrorDataReceived += Process_ErrorDataReceived;
                _process.StartInfo.FileName = "cmd.exe";
                _process.StartInfo.Arguments =
                    "/c \"" + Path.Combine(_rmqDirectory, @"sbin\rabbitmq-server.bat") + "\"";

                WriteLineToOutput(" Server started ... ", Color.White);

                _process.Start();
                _process.BeginOutputReadLine();

                startServerToolStripMenuItem.Enabled = false;
                stopServerToolStripMenuItem.Enabled = true;

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(@"Error starting server\n\n" + ex.Message, Text, MessageBoxButtons.OK,
                    MessageBoxIcon.Stop);
                return false;
            }
        }

        /// <summary>
        ///     Stop RabbitMQ server
        /// </summary>
        /// <returns>True if succesfull</returns>
        private bool StopServer()
        {
            try
            {
                if (_process != null && !_process.HasExited)
                {
                    _process.Kill(); // Kill main process

                    // OK.. Now check if Erlang leave something behind .. ?!
                    var allProcesses = Process.GetProcesses();
                    foreach (var p in allProcesses)
                        try
                        {
                            var fullPath = p.MainModule.FileName;
                            if (fullPath.StartsWith(_erlangDirectory))
                            {
                                Debug.WriteLine("Force to kill : " + fullPath);
                                p.Kill();
                            }
                        }
                        catch (Exception ex)
                        {
                        }
                }

                _process = null;

                startServerToolStripMenuItem.Enabled = true;
                stopServerToolStripMenuItem.Enabled = false;

                WriteLineToOutput(" Server stopped ... ", Color.White);

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(@"Error stopping server\n\n" + ex.Message, Text, MessageBoxButtons.OK,
                    MessageBoxIcon.Stop);
                return false;
            }
        }

        /// <summary>
        ///     Write line in specified color to output window
        /// </summary>
        /// <param name="data">String to be written</param>
        /// <param name="c">Color to be used</param>
        private void WriteLineToOutput(string data, Color c)
        {
            richTextBox1.SelectionColor = c;
            if (data != null)
                richTextBox1.AppendText(data + "\n");
            richTextBox1.ScrollToCaret();
        }

        private void ExitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void AboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            new AboutBox().ShowDialog();
        }
    }
}