using System;
using System.Collections;
using System.Threading;
using Microsoft.SPOT;
using Microsoft.SPOT.Presentation;
using Microsoft.SPOT.Presentation.Controls;
using Microsoft.SPOT.Presentation.Media;
using Microsoft.SPOT.Presentation.Shapes;
using Microsoft.SPOT.Touch;
using GHI.Glide.UI;
using Gadgeteer.Networking;
using GT = Gadgeteer;
using GTM = Gadgeteer.Modules;
using Gadgeteer.Modules.GHIElectronics;
using GHI.SQLite;
using GHI.Processor;
using Microsoft.SPOT.Hardware;
using GHI.Glide;
using GHI.Glide.Geom;

namespace Gadgeteer.SQLite
{
    public partial class Program
    {
        //UI
        GHI.Glide.UI.DataGrid dataGrid = null;
        GHI.Glide.UI.Button btnReset = null;
        GHI.Glide.Display.Window window = null;
        // This method is run when the mainboard is powered up or reset.   
        void ProgramStarted()
        {
            multicolorLED.BlinkOnce(GT.Color.Red);
            
            //7" Displays
            Display.Width = 800;
            Display.Height = 480;
            Display.OutputEnableIsFixed = false;
            Display.OutputEnablePolarity = true;
            Display.PixelPolarity = false;
            Display.PixelClockRateKHz = 30000;
            Display.HorizontalSyncPolarity = false;
            Display.HorizontalSyncPulseWidth = 48;
            Display.HorizontalBackPorch = 88;
            Display.HorizontalFrontPorch = 40;
            Display.VerticalSyncPolarity = false;
            Display.VerticalSyncPulseWidth = 3;
            Display.VerticalBackPorch = 32;
            Display.VerticalFrontPorch = 13;
            Display.Type = Display.DisplayType.Lcd;
            if (Display.Save())      // Reboot required?
            {
                PowerState.RebootDevice(false);
            }
            //set up touch screen
            CapacitiveTouchController.Initialize(GHI.Pins.FEZRaptor.Socket13.Pin3);
            //displayNHVN.Configure7InchDisplay();
            
            window = GlideLoader.LoadWindow(Resources.GetString(Resources.StringResources.MyForm));
            //glide init
            GlideTouch.Initialize();

            btnReset = (GHI.Glide.UI.Button)window.GetChildByName("btnReset");
            dataGrid = (GHI.Glide.UI.DataGrid)window.GetChildByName("dataGrid");
           
            Glide.MainWindow = window;
            Thread th1 = new Thread(new ThreadStart(Looping));
            th1.Start();
        }

       

        void Looping()
        {
            //create grid column
            dataGrid.AddColumn(new DataGridColumn("Room", 200));
            dataGrid.AddColumn(new DataGridColumn("Time", 200));
            dataGrid.AddColumn(new DataGridColumn("Light", 200));

            String[] origin_names = null;
            ArrayList tabledata = null;
            // Create a database in memory,
            // file system is possible however!
            Database myDatabase = new GHI.SQLite.Database();
            myDatabase.ExecuteNonQuery("CREATE Table Lights" +
            " (Room TEXT, Time TEXT, Value DOUBLE)");
            //reset database n display
            btnReset.TapEvent += (object sender) =>
            {
                myDatabase.ExecuteNonQuery("DELETE FROM Lights");
                dataGrid.Clear();
                dataGrid.Invalidate();
            };
            while (true)
            {
                //get sensor value
                var LightValue = lightSense.ReadProportion();
                var TimeStr = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var item = new DataGridItem(new object[] { "My Room", TimeStr, LightValue });
                //add data to grid
                dataGrid.AddItem(item);
                dataGrid.Invalidate();
                //add rows to table
                myDatabase.ExecuteNonQuery("INSERT INTO Lights (Room, Time, Value)" +
                " VALUES ('My Room', '" + TimeStr + "' , " + LightValue + ")");

                // Process SQL query and save returned records in SQLiteDataTable
                ResultSet result = myDatabase.ExecuteQuery("SELECT * FROM Lights");
                // Get a copy of columns orign names example
                origin_names = result.ColumnNames;
                // Get a copy of table data example
                tabledata = result.Data;
                String fields = "Fields: ";
                for (int i = 0; i < result.ColumnCount; i++)
                {
                    fields += result.ColumnNames[i] + " |";
                }
                Debug.Print(fields);
                object obj;
                String row = "";
                for (int j = 0; j < result.RowCount; j++)
                {
                    row = j.ToString() + " ";
                    for (int i = 0; i < result.ColumnCount; i++)
                    {
                        obj = result[j, i];
                        if (obj == null)
                            row += "N/A";
                        else
                            row += obj.ToString();
                        row += " |";
                    }
                    Debug.Print(row);
                }

                Thread.Sleep(3000);
            }
            myDatabase.Dispose();
        }

       
    }


    //driver for touch screen
    public class CapacitiveTouchController
    {
        private InterruptPort touchInterrupt;
        private I2CDevice i2cBus;
        private I2CDevice.I2CTransaction[] transactions;
        private byte[] addressBuffer;
        private byte[] resultBuffer;

        private static CapacitiveTouchController _this;

        public static void Initialize(Cpu.Pin PortId)
        {
            if (_this == null)
                _this = new CapacitiveTouchController(PortId);
        }

        private CapacitiveTouchController()
        {
        }

        private CapacitiveTouchController(Cpu.Pin portId)
        {
            transactions = new I2CDevice.I2CTransaction[2];
            resultBuffer = new byte[1];
            addressBuffer = new byte[1];
            i2cBus = new I2CDevice(new I2CDevice.Configuration(0x38, 400));
            touchInterrupt = new InterruptPort(portId, false, Port.ResistorMode.Disabled, Port.InterruptMode.InterruptEdgeBoth);
            touchInterrupt.OnInterrupt += (a, b, c) => this.OnTouchEvent();
        }

        private void OnTouchEvent()
        {
            for (var i = 0; i < 5; i++)
            {
                var first = this.ReadRegister((byte)(3 + i * 6));
                var x = ((first & 0x0F) << 8) + this.ReadRegister((byte)(4 + i * 6));
                var y = ((this.ReadRegister((byte)(5 + i * 6)) & 0x0F) << 8) + this.ReadRegister((byte)(6 + i * 6));

                if (x == 4095 && y == 4095)
                    break;

                if (((first & 0xC0) >> 6) == 1)
                    GlideTouch.RaiseTouchUpEvent(null, new GHI.Glide.TouchEventArgs(new Point(x, y)));
                else
                    GlideTouch.RaiseTouchDownEvent(null, new GHI.Glide.TouchEventArgs(new Point(x, y)));

            }
        }

        private byte ReadRegister(byte address)
        {
            this.addressBuffer[0] = address;

            this.transactions[0] = I2CDevice.CreateWriteTransaction(this.addressBuffer);
            this.transactions[1] = I2CDevice.CreateReadTransaction(this.resultBuffer);

            this.i2cBus.Execute(this.transactions, 1000);

            return this.resultBuffer[0];
        }
    }

}
