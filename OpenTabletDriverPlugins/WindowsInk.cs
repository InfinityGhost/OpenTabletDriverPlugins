using System.Collections.Generic;
using System.Linq;
using TabletDriverPlugin;
using TabletDriverPlugin.Attributes;
using TabletDriverPlugin.Tablet;
using TabletDriverPlugin.Platform.Display;
using HidSharp;
using System;
using System.Runtime.InteropServices;

namespace TabletDriverPlugins
{
    [PluginName("Windows Ink"), SupportedPlatform(PluginPlatform.Windows)]
    public class WindowsInk : IBindingHandler<IBinding>, IAbsoluteMode
    {
        public void Read(IDeviceReport report)
        {
            if (!isOpen)
                GetVirtualDevice();
            if (isOpen && report is ITabletReport tabletReport)
                Position(tabletReport);
        }

        private const float LogicalMinimum = 0.0f;
        private const float LogicalMaximum = 32767.0f;
        
        private Area _displayArea, _tabletArea;
        private IVirtualScreen _virtualScreen;
        private TabletProperties _tabletProperties;
        private bool isOpen = false;
        private float[] _rotationMatrix;
        private float _halfDisplayWidth, _halfDisplayHeight, _halfTabletWidth, _halfTabletHeight;
        private float _minX, _maxX, _minY, _maxY;
        private IList<bool> PenButtonStates = new bool[2];
        private IList<bool> AuxButtonStates = new bool[6];
        private static Lazy<InkBindingHandler> _inkBinding = new Lazy<InkBindingHandler>();

        public Area Output
        {
            set
            {
                _displayArea = value;
                UpdateCache();
            }
            get => _displayArea;
        }

        public Area Input
        {
            set
            {
                _tabletArea = value;
                UpdateCache();
            }
            get => _tabletArea;
        }

        public IVirtualScreen VirtualScreen
        {
            set
            {
                _virtualScreen = value;
            }
            get => _virtualScreen;
        }

        public TabletProperties TabletProperties
        {
            set
            {
                _tabletProperties = value;
                UpdateCache();
            }
            get => _tabletProperties;
        }

        private IEnumerable<IFilter> _filters, _preFilters, _postFilters;
        public IEnumerable<IFilter> Filters
        {
            set
            {
                _filters = value;
                _preFilters = value.Where(f => f.FilterStage == FilterStage.PreTranspose);
                _postFilters = value.Where(f => f.FilterStage == FilterStage.PostTranspose);
            }
            get => _filters;
        }

        public bool AreaClipping { set; get; }

        // Goes unused, WindowsInk expects a touch on pressure > 0. Need to keep it because we inherit IAbsoluteMode
        public float TipActivationPressure { set; get; } 
        public IBinding TipBinding { set; get; } = null;
        public Dictionary<int, IBinding> PenButtonBindings { set; get; } = new Dictionary<int, IBinding>();
        public Dictionary<int, IBinding> AuxButtonBindings { set; get; } = new Dictionary<int, IBinding>();
        
        internal static InkBindingHandler InkBinding => _inkBinding.Value;
        
        private HidDevice VMultiDevice { set; get; }
        private HidStream VMultiStream { set; get; }

        private void UpdateCache()
        {
            _rotationMatrix = Input?.GetRotationMatrix();

            _halfDisplayWidth = Output?.Width / 2 ?? 0;
            _halfDisplayHeight = Output?.Height / 2 ?? 0;
            _halfTabletWidth = Input?.Width / 2 ?? 0;
            _halfTabletHeight = Input?.Height / 2 ?? 0;

            _minX = Output?.Position.X - _halfDisplayWidth ?? 0;
            _maxX = Output?.Position.X + Output?.Width - _halfDisplayWidth ?? 0;
            _minY = Output?.Position.Y - _halfDisplayHeight ?? 0;
            _maxY = Output?.Position.Y + Output?.Height - _halfDisplayHeight ?? 0;
        }

        private unsafe byte[] GetBytes(InkReport str)
        {
            InkReport* report = &str;
            int size = sizeof(InkReport);
            byte[] data = new byte[size];
            Marshal.Copy((IntPtr)report, data, 0, size);
            return data;
        }

        public void GetVirtualDevice()
        {
            // Connect to HID device from XP-Pen drivers
            var devices = DeviceList.Local.GetHidDevices();
            var matching = devices.Where(d => d.GetMaxOutputReportLength() == 65 & d.GetMaxInputReportLength() == 65);
            var tabletDevice = matching.FirstOrDefault(d => d.ProductID == 47820);

            if (tabletDevice != null)
            {
                isOpen = Open(tabletDevice);
            }
            else
            {
                Log.Write("WindowsInk", "Failed to find the Virtual Tablet." + 
                    "Install the XP-Pen VMulti driver.", true);
            }
        }

        private bool Open(HidDevice device)
        {
            VMultiDevice = device;
            if (VMultiDevice != null)
            {
                var config = new OpenConfiguration();
                config.SetOption(OpenOption.Priority, OpenPriority.Low);
                if (VMultiDevice.TryOpen(config, out var stream))
                {
                    VMultiStream = stream;
                    return true;
                }
                else
                {
                    Log.Write("WindowsInk", "Failed to open the Virtual Tablet." +
                        "Check to make sure that the XP-Pen VMulti driver isn't in use.", true);
                    return false;
                }
            }
            else
            {
                return false;
            }
        }

        public void HandleBinding(IDeviceReport report)
        {
            if (report is ITabletReport tabletReport && tabletReport.ReportID >= TabletProperties.ActiveReportID)
                HandlePenBinding(tabletReport);
            if (report is IAuxReport auxReport)
                HandleAuxBinding(auxReport);
        }

        private void HandlePenBinding(ITabletReport report)
        {
            for (var penButton = 0; penButton < 2; penButton++)
            {
                if (PenButtonBindings.TryGetValue(penButton, out var binding) && binding != null)
                {
                    if (report.PenButtons[penButton] && !PenButtonStates[penButton])
                        binding.Press();
                    else if (!report.PenButtons[penButton] && PenButtonStates[penButton])
                        binding.Release();
                }
                PenButtonStates[penButton] = report.PenButtons[penButton];
            }
        }

        private void HandleAuxBinding(IAuxReport report)
        {
            for (var auxButton = 0; auxButton < 6; auxButton++)
            {
                if (AuxButtonBindings.TryGetValue(auxButton, out var binding) && binding != null)
                {
                    if (report.AuxButtons[auxButton] && !AuxButtonStates[auxButton])
                        binding.Press();
                    else if (!report.AuxButtons[auxButton] && AuxButtonStates[auxButton])
                        binding.Release();
                }
                AuxButtonStates[auxButton] = report.AuxButtons[auxButton];
            }
        }

        public void Position(ITabletReport report)
        {
            if (TabletProperties.ActiveReportID != 0 && report.ReportID <= TabletProperties.ActiveReportID)
                return;

            var pos = new Point(report.Position.X, report.Position.Y);

            // Pre Filter
            foreach (IFilter filter in _preFilters)
                pos = filter.Filter(pos);

            // Normalize (ratio of 1)
            pos.X /= TabletProperties.MaxX;
            pos.Y /= TabletProperties.MaxY;

            // Scale to tablet dimensions (mm)
            pos.X *= TabletProperties.Width;
            pos.Y *= TabletProperties.Height;

            // Adjust area to set origin to 0,0
            pos -= Input.Position;

            // Rotation
            if (Input.Rotation != 0f)
            {
                var tempCopy = new Point(pos.X, pos.Y);
                pos.X = (tempCopy.X * _rotationMatrix[0]) + (tempCopy.Y * _rotationMatrix[1]);
                pos.Y = (tempCopy.X * _rotationMatrix[2]) + (tempCopy.Y * _rotationMatrix[3]);
            }

            // Move area back
            pos.X += _halfTabletWidth;
            pos.Y += _halfTabletHeight;

            // Scale to tablet area (ratio of 1)
            pos.X /= Input.Width;
            pos.Y /= Input.Height;

            // Scale to display area
            pos.X *= Output.Width;
            pos.Y *= Output.Height;

            // Adjust display offset by center
            pos.X += Output.Position.X - _halfDisplayWidth;
            pos.Y += Output.Position.Y - _halfDisplayHeight;

            // Clipping to display bounds
            if (AreaClipping)
            {
                if (pos.X < _minX)
                    pos.X = _minX;
                if (pos.X > _maxX)
                    pos.X = _maxX;
                if (pos.Y < _minY)
                    pos.Y = _minY;
                if (pos.Y > _maxY)
                    pos.Y = _maxY;
            }

            // Post Filter
            foreach (IFilter filter in _postFilters)
                pos = filter.Filter(pos);

            // Setting report position based on Logical Maximum
            double offsetX = -(LogicalMaximum / Output.Width);
            double offsetY = -(LogicalMaximum / Output.Height);

            // Move from pixel positioning to windows ink positioning (0 to 32767)
            var pos_x = Math.Round(pos.X / _virtualScreen.Width * LogicalMaximum + offsetX);
            var pos_y = Math.Round(pos.Y / _virtualScreen.Height * LogicalMaximum + offsetY);

            // Clipping to logical bounds
            if (pos_x < LogicalMinimum)
                pos_x = LogicalMinimum;
            if (pos_x > LogicalMaximum)
                pos_x = LogicalMaximum;
            if (pos_y < LogicalMinimum)
                pos_y = LogicalMinimum;
            if (pos_y > LogicalMaximum)
                pos_y = LogicalMaximum;

            // Normalize pressure
            double normpressure = (double)report.Pressure / (double)TabletProperties.MaxPressure;
            // Expand pressure to fit driver supported pressure levels
            double pressure = Math.Round(normpressure * 8191.0);
            
            int tipState = 0;
            // We need to send an out of range report when switching to and from eraser mode. 
            // To satisfy windows completely we will lift the pen, exit ranhe, hover again, and then touch.
            // https://docs.microsoft.com/en-us/windows-hardware/design/component-guidelines/windows-pen-states
            switch (InkBinding.PenStatus)
            {
                case "Lift-Out":
                    pressure = 0;
                    tipState |= 1 << (int)BitPositions.InRange;
                    InkBinding.PenStatus = "OutRange";
                    break;
                case "OutRange":
                    pressure = 0;
                    InkBinding.PenStatus = "Lift-In";
                    break;
                case "Lift-In":
                    pressure = 0;
                    tipState |= 1 << (int)BitPositions.InRange;
                    InkBinding.PenStatus = string.Empty;
                    break;
                default:
                    tipState |= 1 << (int)BitPositions.InRange;
                    break;
            }

            // Setting button bit switches
            if (InkBinding.PenKeys["Eraser"])
            {
                tipState |= 1 << (int)BitPositions.Invert; 
            }

            if (InkBinding.PenKeys["Barrel"])
            {
                tipState |= 1 << (int)BitPositions.Barrel;
            }

            if (pressure != 0)
            {
                tipState |= InkBinding.PenKeys["Eraser"] ? 1 << (int)BitPositions.Eraser : 1 << (int)BitPositions.Press;
            }

            // Setting the report values
            var inkReport = new InkReport
            {
                vmultiId = 0x40,
                reportLength = 0XA,
                reportId = 0X5,
                buttons = (byte)tipState,
                X = (ushort)pos_x,
                Y = (ushort)pos_y,
                pressure = (ushort)pressure
            };

            try
            {
                var data = GetBytes(inkReport);
                VMultiStream.Write(data);
            }
            catch (Exception e)
            {
                Log.Exception(e);
            }
        }

        internal class InkBindingHandler
        {
            private void KeyPress(string key, bool isPress)
            {
                PenKeys[key] = isPress;
                PenStatus = "Lift-Out";
            }

            public void KeyToggle(string key, bool isPressed)
            {
                
                var newkey = key.Replace("Toggle", string.Empty);
                if (isPressed & !PenKeys[key] )
                {
                    PenKeys[newkey] = !PenKeys[newkey];
                    PenStatus = "Lift-Out";
                }
                PenKeys[key] = isPressed;
            }

            public void Press(string key)
            {
                if (Supportedkeys.Contains(key))
                {
                    Log.Write("WindowsInk", "Unsupported key pressed");
                    return;
                }
                if (key.Contains("Toggle"))
                {
                    KeyToggle(key, true);
                }
                else
                {
                    KeyPress(key, true);
                }
            }
            public void Release(string key)
            {
                if (Supportedkeys.Contains(key))
                {
                    Log.Write("WindowsInk", "Unsupported key pressed");
                    return;
                }
                if (key.Contains("Toggle"))
                {
                    KeyToggle(key, false);
                }
                else
                {
                    KeyPress(key, false);
                }
            }

            public Dictionary<string, bool> PenKeys = new Dictionary<string, bool>
            {
                { "Eraser", false },
                { "EraserToggle", false },
                { "Barrel", false },
                { "BarrelToggle", false },
            };

            public string PenStatus = string.Empty;

            public List<string> Supportedkeys = new List<string>
            {
                { "Eraser" },
                { "EraserToggle" },
                { "Barrel" },
                { "BarrelToggle" },
            };
        }

        // HID report structure for XP-Pen drivers. It is used to send Windows Ink packets.
        private struct InkReport
        {
            public Byte vmultiId;       // ID to communicate to the Digitizer device
            public Byte reportLength;   // Size of the report in bytes.
            public Byte reportId;       // ID of the report.
            public Byte buttons;        // Byte with switches for pen buttons / states
            public ushort X;            // X position of the pen from 0 to 32767
            public ushort Y;            // Y position of the pen from 0 to 32767
            public ushort pressure;     // Pressure level from 0 to 8191
        }

        internal enum BitPositions : int
        {
            Press = 0,
            Barrel = 1,
            Eraser = 2,
            Invert = 3,
            InRange = 4
        }
    }

    [PluginName("Windows Ink Binding")]
    public class WindowsInkBinding : IBinding
    {
        public string Name
        {
            get
            {
                return nameof(WindowsInkBinding) + ": " + Property;
            }
        }

        public string Property { set; get; }

        public Action Press
        {
            get
            {
                WindowsInk.InkBindingHandler PenHandler = WindowsInk.InkBinding;
                return () => PenHandler.Press(Property);
            }
        }

        public Action Release
        {
            get
            {
                WindowsInk.InkBindingHandler PenHandler = WindowsInk.InkBinding;
                return () => PenHandler.Release(Property);
            }
        }

        public override string ToString() => Name;
    }
}