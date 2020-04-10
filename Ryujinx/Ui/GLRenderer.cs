using Gdk;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using Ryujinx.Configuration;
using Ryujinx.Common.Configuration.Hid;
using Ryujinx.Graphics.OpenGL;
using Ryujinx.HLE;
using Ryujinx.HLE.HOS.Services.Hid;
using System;
using System.Linq;
using System.Threading;

namespace Ryujinx.Ui
{
    public class GlRenderer : GLWidget
    {
        private const int SwitchPanelWidth  = 1280;
        private const int SwitchPanelHeight = 720;
        private const int TargetFps         = 60;

        public ManualResetEvent WaitEvent { get; set; }

        public static event EventHandler<StatusUpdatedEventArgs> StatusUpdatedEvent;

        public bool IsActive   { get; set; }
        public bool IsStopped  { get; set; }
        public bool IsFocused  { get; set; }

        private double _mouseX;
        private double _mouseY;
        private bool   _mousePressed;

        private bool _toggleFullscreen;

        private readonly long _ticksPerFrame;

        private long _ticks = 0;

        private System.Diagnostics.Stopwatch _chrono;

        private Switch _device;

        private Renderer _renderer;

        private HotkeyButtons _prevHotkeyButtons;

        public GlRenderer(Switch device)
            : base (GetGraphicsMode(),
            3, 3,
            GraphicsContextFlags.ForwardCompatible)
        {
            WaitEvent = new ManualResetEvent(false);

            _device = device;

            this.Initialized  += GLRenderer_Initialized;
            this.Destroyed    += GLRenderer_Destroyed;
            this.ShuttingDown += GLRenderer_ShuttingDown;

            Initialize();

            _chrono = new System.Diagnostics.Stopwatch();

            _ticksPerFrame = System.Diagnostics.Stopwatch.Frequency / TargetFps;

            AddEvents((int)(EventMask.ButtonPressMask
                          | EventMask.ButtonReleaseMask
                          | EventMask.PointerMotionMask
                          | EventMask.KeyPressMask
                          | EventMask.KeyReleaseMask));

            this.Shown += Renderer_Shown;
        }

        private static GraphicsMode GetGraphicsMode()
        {
            return Environment.OSVersion.Platform == PlatformID.Unix ? new GraphicsMode(new ColorFormat(24)) : new GraphicsMode(new ColorFormat());
        }

        private void GLRenderer_ShuttingDown(object sender, EventArgs args)
        {
            _device.DisposeGpu();
        }

        private void Parent_FocusOutEvent(object o, Gtk.FocusOutEventArgs args)
        {
            IsFocused = false;
        }

        private void Parent_FocusInEvent(object o, Gtk.FocusInEventArgs args)
        {
            IsFocused = true;
        }

        private void GLRenderer_Destroyed(object sender, EventArgs e)
        {
            Dispose();
        }

        protected void Renderer_Shown(object sender, EventArgs e)
        {
            IsFocused = this.ParentWindow.State.HasFlag(Gdk.WindowState.Focused);
        }

        public void HandleScreenState()
        {
            KeyboardState keyboard = OpenTK.Input.Keyboard.GetState();

            bool toggleFullscreen =  keyboard.IsKeyDown(OpenTK.Input.Key.F11)
                                || ((keyboard.IsKeyDown(OpenTK.Input.Key.AltLeft)
                                ||   keyboard.IsKeyDown(OpenTK.Input.Key.AltRight))
                                &&   keyboard.IsKeyDown(OpenTK.Input.Key.Enter))
                                ||   keyboard.IsKeyDown(OpenTK.Input.Key.Escape);

            bool fullScreenToggled = ParentWindow.State.HasFlag(Gdk.WindowState.Fullscreen);

            if (toggleFullscreen != _toggleFullscreen)
            {
                if (toggleFullscreen)
                {
                    if (fullScreenToggled)
                    {
                        ParentWindow.Unfullscreen();
                        (Toplevel as MainWindow)?.ToggleExtraWidgets(true);
                    }
                    else
                    {
                        if (keyboard.IsKeyDown(OpenTK.Input.Key.Escape))
                        {
                            if (GtkDialog.CreateExitDialog())
                            {
                                Exit();
                            }
                        }
                        else
                        {
                            ParentWindow.Fullscreen();
                            (Toplevel as MainWindow)?.ToggleExtraWidgets(false);
                        }
                    }
                }
            }

            _toggleFullscreen = toggleFullscreen;
        }

        private void GLRenderer_Initialized(object sender, EventArgs e)
        {
            // Release the GL exclusivity that OpenTK gave us as we aren't going to use it in GTK Thread.
            GraphicsContext.MakeCurrent(null);

            WaitEvent.Set();
        }

        protected override bool OnConfigureEvent(EventConfigure evnt)
        {
            bool result = base.OnConfigureEvent(evnt);

            Gdk.Monitor monitor = Display.GetMonitorAtWindow(Window);

            _renderer.Window.SetSize(evnt.Width * monitor.ScaleFactor, evnt.Height * monitor.ScaleFactor);

            return result;
        }

        public void Start()
        {
            IsRenderHandler = true;

            _chrono.Restart();

            IsActive = true;

            Gtk.Window parent = this.Toplevel as Gtk.Window;

            parent.FocusInEvent  += Parent_FocusInEvent;
            parent.FocusOutEvent += Parent_FocusOutEvent;

            Gtk.Application.Invoke(delegate
            {
                parent.Present();

                string titleNameSection = string.IsNullOrWhiteSpace(_device.System.TitleName) ? string.Empty
                    : " | " + _device.System.TitleName;

                string titleIdSection = string.IsNullOrWhiteSpace(_device.System.TitleIdText) ? string.Empty
                    : " | " + _device.System.TitleIdText.ToUpper();

                parent.Title = $"Ryujinx {Program.Version}{titleNameSection}{titleIdSection}";
            });

            Thread renderLoopThread = new Thread(Render)
            {
                Name = "GUI.RenderLoop"
            };
            renderLoopThread.Start();

            MainLoop();

            renderLoopThread.Join();

            Exit();
        }

        protected override bool OnButtonPressEvent(EventButton evnt)
        {
            _mouseX = evnt.X;
            _mouseY = evnt.Y;

            if (evnt.Button == 1)
            {
                _mousePressed = true;
            }

            return false;
        }

        protected override bool OnButtonReleaseEvent(EventButton evnt)
        {
            if (evnt.Button == 1)
            {
                _mousePressed = false;
            }

            return false;
        }

        protected override bool OnMotionNotifyEvent(EventMotion evnt)
        {
            if (evnt.Device.InputSource == InputSource.Mouse)
            {
                _mouseX = evnt.X;
                _mouseY = evnt.Y;
            }

            return false;
        }

        protected override void OnGetPreferredHeight(out int minimumHeight, out int naturalHeight)
        {
            Gdk.Monitor monitor = Display.GetMonitorAtWindow(Window);

            // If the monitor is at least 1080p, use the Switch panel size as minimal size.
            if (monitor.Geometry.Height >= 1080)
            {
                minimumHeight = SwitchPanelHeight;
            }
            // Otherwise, we default minimal size to 480p 16:9.
            else
            {
                minimumHeight = 480;
            }

            naturalHeight = minimumHeight;
        }

        protected override void OnGetPreferredWidth(out int minimumWidth, out int naturalWidth)
        {
            Gdk.Monitor monitor = Display.GetMonitorAtWindow(Window);

            // If the monitor is at least 1080p, use the Switch panel size as minimal size.
            if (monitor.Geometry.Height >= 1080)
            {
                minimumWidth = SwitchPanelWidth;
            }
            // Otherwise, we default minimal size to 480p 16:9.
            else
            {
                minimumWidth = 854;
            }

            naturalWidth = minimumWidth;
        }

        public void Exit()
        {
            if (IsStopped)
            {
                return;
            }

            IsStopped = true;
            IsActive  = false;
        }

        public void Initialize()
        {
            if (!(_device.Gpu.Renderer is Renderer))
            {
                throw new NotSupportedException($"GPU renderer must be an OpenGL renderer when using GLRenderer!");
            }

            _renderer = (Renderer)_device.Gpu.Renderer;
        }

        public void Render()
        {
            // First take exclusivity on the OpenGL context.
            GraphicsContext.MakeCurrent(WindowInfo);

            _renderer.Initialize();

            // Make sure the first frame is not transparent.
            GL.ClearColor(OpenTK.Color.Black);
            GL.Clear(ClearBufferMask.ColorBufferBit);
            SwapBuffers();

            while (IsActive)
            {
                if (IsStopped)
                {
                    return;
                }

                _ticks += _chrono.ElapsedTicks;

                _chrono.Restart();

                if (_device.WaitFifo())
                {
                    _device.ProcessFrame();
                }

                if (_ticks >= _ticksPerFrame)
                {
                    _device.PresentFrame(SwapBuffers);

                    _device.Statistics.RecordSystemFrameTime();

                    StatusUpdatedEvent?.Invoke(this, new StatusUpdatedEventArgs(
                        _device.EnableDeviceVsync, 
                        $"Host: {_device.Statistics.GetSystemFrameRate():00.00} FPS", 
                        $"Game: {_device.Statistics.GetGameFrameRate():00.00} FPS",
                        $"GPU:  {_renderer.GpuVendor}"));

                    _ticks = Math.Min(_ticks - _ticksPerFrame, _ticksPerFrame);
                }
            }
        }

        public void SwapBuffers()
        {
            OpenTK.Graphics.GraphicsContext.CurrentContext.SwapBuffers();
        }

        public void MainLoop()
        {
            while (IsActive)
            {
                UpdateFrame();

                // Polling becomes expensive if it's not slept
                Thread.Sleep(1);
            }
        }

        private bool UpdateFrame()
        {
            if (!IsActive)
            {
                return true;
            }

            if (IsStopped)
            {
                return false;
            }

            int numControllers = ConfigurationState.Instance.Hid.InputConfig.Value.Count;

            ControllerKeys[]   currentButton = new ControllerKeys[numControllers];
            JoystickPosition[] leftJoystick  = new JoystickPosition[numControllers];
            JoystickPosition[] rightJoystick = new JoystickPosition[numControllers];
            KeyboardInput?[]   hidKeyboard   = new KeyboardInput?[numControllers];

            int[] leftJoystickDx  = new int[numControllers];
            int[] leftJoystickDy  = new int[numControllers];
            int[] rightJoystickDx = new int[numControllers];
            int[] rightJoystickDy = new int[numControllers];

            _device.Hid.Npads.SetGamepadsInput(Enumerable.Range(0, numControllers).Select(index =>
            {
                if (ConfigurationState.Instance.Hid.InputConfig.Value[index] is KeyboardConfig keyboardConfig)
                {
                    // Keyboard Input
                    KeyboardController keyboardController = new KeyboardController(keyboardConfig);

                    currentButton[index] = keyboardController.GetButtons();

                    if (ConfigurationState.Instance.Hid.EnableKeyboard)
                    {
                        hidKeyboard[index] = keyboardController.GetKeysDown();
                    }

                    (leftJoystickDx[index],  leftJoystickDy[index])  = keyboardController.GetLeftStick();
                    (rightJoystickDx[index], rightJoystickDy[index]) = keyboardController.GetRightStick();

                    leftJoystick[index] = new JoystickPosition
                    {
                        Dx = leftJoystickDx[index],
                        Dy = leftJoystickDy[index]
                    };

                    rightJoystick[index] = new JoystickPosition
                    {
                        Dx = rightJoystickDx[index],
                        Dy = rightJoystickDy[index]
                    };

                    if (hidKeyboard[index] == null)
                    {
                        hidKeyboard[index] = new KeyboardInput
                        {
                            Modifier = 0,
                            Keys     = new int[0x8]
                        };
                    }

                    if (ConfigurationState.Instance.Hid.EnableKeyboard && hidKeyboard[index] != null)
                    {
                        _device.Hid.Keyboard.Update(hidKeyboard[index].Value);
                    }

                    // Toggle vsync
                    HotkeyButtons currentHotkeyButtons = keyboardController.GetHotkeyButtons();

                    if (currentHotkeyButtons.HasFlag(HotkeyButtons.ToggleVSync) &&
                        !_prevHotkeyButtons.HasFlag(HotkeyButtons.ToggleVSync))
                    {
                        _device.EnableDeviceVsync = !_device.EnableDeviceVsync;
                    }

                    _prevHotkeyButtons = currentHotkeyButtons;
                }
                else if (ConfigurationState.Instance.Hid.InputConfig.Value[index] is Common.Configuration.Hid.ControllerConfig controllerConfig)
                {
                    // Controller Input
                    JoystickController joystickController = new JoystickController(controllerConfig);

                    currentButton[index] |= joystickController.GetButtons();

                    (leftJoystickDx[index],  leftJoystickDy[index])  = joystickController.GetLeftStick();
                    (rightJoystickDx[index], rightJoystickDy[index]) = joystickController.GetRightStick();

                    leftJoystick[index] = new JoystickPosition
                    {
                        Dx = controllerConfig.LeftJoycon.InvertStickX ? -leftJoystickDx[index] : leftJoystickDx[index],
                        Dy = controllerConfig.LeftJoycon.InvertStickY ? -leftJoystickDy[index] : leftJoystickDy[index]
                    };

                    rightJoystick[index] = new JoystickPosition
                    {
                        Dx = controllerConfig.RightJoycon.InvertStickX ? -rightJoystickDx[index] : rightJoystickDx[index],
                        Dy = controllerConfig.RightJoycon.InvertStickY ? -rightJoystickDy[index] : rightJoystickDy[index]
                    };
                }

                currentButton[index] |= _device.Hid.UpdateStickButtons(leftJoystick[index], rightJoystick[index]);
                
                return new GamepadInput
                {
                    PlayerId = ConfigurationState.Instance.Hid.InputConfig.Value[index].PlayerIndex,
                    Buttons  = currentButton[index],
                    LStick   = leftJoystick[index],
                    RStick   = rightJoystick[index]
                };
            }).ToArray());

            Gtk.Application.Invoke(delegate
            {
                HandleScreenState();
            });

            //Touchscreen
            bool hasTouch = false;

            // Get screen touch position from left mouse click
            // OpenTK always captures mouse events, even if out of focus, so check if window is focused.
            if (IsFocused && _mousePressed)
            {
                int screenWidth  = AllocatedWidth;
                int screenHeight = AllocatedHeight;

                if (AllocatedWidth > (AllocatedHeight * SwitchPanelWidth) / SwitchPanelHeight)
                {
                    screenWidth = (AllocatedHeight * SwitchPanelWidth) / SwitchPanelHeight;
                }
                else
                {
                    screenHeight = (AllocatedWidth * SwitchPanelHeight) / SwitchPanelWidth;
                }

                int startX = (AllocatedWidth  - screenWidth)  >> 1;
                int startY = (AllocatedHeight - screenHeight) >> 1;

                int endX = startX + screenWidth;
                int endY = startY + screenHeight;


                if (_mouseX >= startX &&
                    _mouseY >= startY &&
                    _mouseX < endX &&
                    _mouseY < endY)
                {
                    int screenMouseX = (int)_mouseX - startX;
                    int screenMouseY = (int)_mouseY - startY;

                    int mX = (screenMouseX * SwitchPanelWidth) / screenWidth;
                    int mY = (screenMouseY * SwitchPanelHeight) / screenHeight;

                    TouchPoint currentPoint = new TouchPoint
                    {
                        X = (uint)mX,
                        Y = (uint)mY,

                        // Placeholder values till more data is acquired
                        DiameterX = 10,
                        DiameterY = 10,
                        Angle = 90
                    };

                    hasTouch = true;

                    _device.Hid.Touchscreen.Update(currentPoint);
                }
            }

            if (!hasTouch)
            {
                _device.Hid.Touchscreen.Update();
            }

            _device.Hid.DebugPad.Update();

            return true;
        }
    }
}
