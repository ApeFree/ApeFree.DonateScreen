using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Master;
using ApeFree.DonateScreen.Rendering;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ApeFree.DonateScreen.WinDesktop.Views
{
    public partial class RemoteViewerWindow : Window
    {
        private readonly SlaveSession _session;
        private readonly WriteableBitmapBlitter _blitter = new();
        private readonly DispatcherTimer _statsTimer;
        private int _fpsCounter = 0;
        private long _bytesCounter = 0;
        private DateTime _lastStatsTime = DateTime.UtcNow;
        private const int TargetQuota = 10;
        private const int RefillThreshold = 6;
        private int _framesReceivedSinceRefill = 0;
        private bool _isFullscreen = false;

        // 鼠标按键与拖拽状态跟踪
        private bool _isLeftDown = false;
        private bool _isRightDown = false;
        private bool _isMiddleDown = false;
        private ushort _lastNormX = 32768;
        private ushort _lastNormY = 32768;

        public RemoteViewerWindow(SlaveSession session)
        {
            InitializeComponent();
            _session = session ?? throw new ArgumentNullException(nameof(session));

            // 恢复标准鼠标指针形状
            Cursor = Cursors.Arrow;
            ImgDisplay.Cursor = Cursors.Arrow;

            Title = $"远程控制 - {_session.Info.MachineName} ({_session.RemoteAddress})";
            TxtDeviceTitle.Text = string.IsNullOrWhiteSpace(_session.Info.MachineName) ? _session.RemoteAddress : _session.Info.MachineName;

            bool isFullControl = _session.CanControl;
            if (isFullControl)
            {
                BadgeMode.Background = new SolidColorBrush(Color.FromRgb(16, 124, 65));
                TxtBadgeMode.Text = "完全控制";
                BorderViewOnlyNotice.Visibility = Visibility.Collapsed;
                TxtStatusTip.Text = "就绪 - 点击画面内即可通过本地键盘鼠标直接操作远端";
            }
            else
            {
                BadgeMode.Background = new SolidColorBrush(Color.FromRgb(0, 120, 212));
                TxtBadgeMode.Text = "仅观看屏幕";
                BorderViewOnlyNotice.Visibility = Visibility.Visible;
                TxtStatusTip.Text = "[只读模式] 当前为仅观看模式，受控端未开放键盘鼠标控制权限";
            }

            // 监听屏幕帧事件
            _session.FrameReceived += OnFrameReceived;

            // 监听位图重建
            _blitter.BitmapReset += (s, bmp) =>
            {
                Dispatcher.Invoke(() =>
                {
                    ImgDisplay.Source = bmp;
                    TxtResolution.Text = $"{bmp.PixelWidth}x{bmp.PixelHeight}";
                });
            };

            // 统计定时器
            _statsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _statsTimer.Tick += (s, e) => UpdateStats();
            _statsTimer.Start();

            // 主动设置流控刷新配额并触发画面推流
            Task.Run(() =>
            {
                try { _session.ScreenService?.SetRefreshQuota(TargetQuota); } catch { }
            });
        }

        private void OnFrameReceived(object? sender, ScreenFramePacketDto e)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _blitter.RenderPacket(e);
                _fpsCounter++;

                if (e.VideoData != null)
                {
                    _bytesCounter += e.VideoData.Length;
                }
                foreach (var b in e.Blocks)
                {
                    _bytesCounter += b.CompressedData?.Length ?? 0;
                }

                // 基于信用额度(Credit)的单调平滑流控机制：
                // 收到达到阈值帧数时，如果窗口未最小化，主动异步通知受控端重置配额为 10
                _framesReceivedSinceRefill++;
                if (_framesReceivedSinceRefill >= RefillThreshold)
                {
                    _framesReceivedSinceRefill = 0;
                    if (WindowState != WindowState.Minimized)
                    {
                        Task.Run(() =>
                        {
                            try { _session.ScreenService?.SetRefreshQuota(TargetQuota); } catch { }
                        });
                    }
                }
            }, DispatcherPriority.Render);
        }

        private void UpdateStats()
        {
            var now = DateTime.UtcNow;
            double elapsed = (now - _lastStatsTime).TotalSeconds;
            if (elapsed >= 1.0)
            {
                int fps = (int)(_fpsCounter / elapsed);
                double kbps = (_bytesCounter / 1024.0) / elapsed;

                TxtFpsInfo.Text = $"FPS: {fps}";
                TxtBitrate.Text = $"{kbps:F1} KB/s";

                _fpsCounter = 0;
                _bytesCounter = 0;
                _lastStatsTime = now;
            }
        }

        #region 键盘与鼠标输入映射

        private bool TryGetNormalizedCoordinates(Point mousePos, out ushort normX, out ushort normY, bool clampToViewport = true)
        {
            normX = _lastNormX;
            normY = _lastNormY;

            if (ImgDisplay.Source == null || ImgDisplay.ActualWidth <= 0 || ImgDisplay.ActualHeight <= 0)
            {
                return false;
            }

            double srcW = ImgDisplay.Source.Width;
            double srcH = ImgDisplay.Source.Height;
            if (srcW <= 0 || srcH <= 0) return false;

            double elemW = ImgDisplay.ActualWidth;
            double elemH = ImgDisplay.ActualHeight;

            // 计算 Uniform 拉伸下的实际画面视口尺寸和偏移
            double scale = Math.Min(elemW / srcW, elemH / srcH);
            double viewW = srcW * scale;
            double viewH = srcH * scale;

            double offsetX = (elemW - viewW) / 2.0;
            double offsetY = (elemH - viewH) / 2.0;

            double clickX = mousePos.X - offsetX;
            double clickY = mousePos.Y - offsetY;

            // 如果不限制在黑边内且未处于按键拖拽状态，当移出有效视口时忽略普通移动
            if (!clampToViewport)
            {
                if (clickX < 0 || clickX > viewW || clickY < 0 || clickY > viewH)
                {
                    return false;
                }
            }

            // 限制在视口边界内 [0, viewW], [0, viewH]
            double clampedX = Math.Clamp(clickX, 0, viewW);
            double clampedY = Math.Clamp(clickY, 0, viewH);

            double ratioX = clampedX / viewW;
            double ratioY = clampedY / viewH;

            normX = (ushort)Math.Clamp((int)(ratioX * 65535), 0, 65535);
            normY = (ushort)Math.Clamp((int)(ratioY * 65535), 0, 65535);
            _lastNormX = normX;
            _lastNormY = normY;
            return true;
        }

        private void SendMouseIfAllowed(MouseActionFlags flags, MouseEventArgs e, int wheelDelta = 0, bool isButtonEvent = false, bool clampToViewport = true)
        {
            if (!_session.CanControl || _session.InputService == null) return;

            var pos = e.GetPosition(ImgDisplay);
            if (TryGetNormalizedCoordinates(pos, out ushort nx, out ushort ny, clampToViewport: clampToViewport || isButtonEvent))
            {
                SendMouseRaw(flags, nx, ny, wheelDelta);
            }
        }

        private void SendMouseRaw(MouseActionFlags flags, ushort normX, ushort normY, int wheelDelta = 0)
        {
            if (!_session.CanControl || _session.InputService == null) return;

            var dto = new MouseEventDto
            {
                NormalizedX = normX,
                NormalizedY = normY,
                Flags = flags,
                WheelDelta = wheelDelta
            };

            // 在后台线程异步派发 RPC，绝不阻塞 WPF UI 交互线程
            Task.Run(() =>
            {
                try
                {
                    _session.InputService?.SendMouseEvent(dto);
                }
                catch { }
            });
        }

        private void OnImageMouseMove(object sender, MouseEventArgs e)
        {
            // 如果处于按住拖拽状态，允许跨越黑边持续跟踪
            bool isDragging = _isLeftDown || _isRightDown || _isMiddleDown;
            SendMouseIfAllowed(MouseActionFlags.Move, e, clampToViewport: isDragging);
        }

        private void OnImageMouseDown(object sender, MouseButtonEventArgs e)
        {
            ImgDisplay.Focus();
            ImgDisplay.CaptureMouse(); // 捕获鼠标，确保拖拽期间持续获取移动和释放事件

            MouseActionFlags flag = e.ChangedButton switch
            {
                MouseButton.Left => MouseActionFlags.LeftDown,
                MouseButton.Right => MouseActionFlags.RightDown,
                MouseButton.Middle => MouseActionFlags.MiddleDown,
                _ => MouseActionFlags.None
            };

            if (e.ChangedButton == MouseButton.Left) _isLeftDown = true;
            if (e.ChangedButton == MouseButton.Right) _isRightDown = true;
            if (e.ChangedButton == MouseButton.Middle) _isMiddleDown = true;

            if (flag != MouseActionFlags.None)
            {
                SendMouseIfAllowed(flag, e, isButtonEvent: true);
            }
        }

        private void OnImageMouseUp(object sender, MouseButtonEventArgs e)
        {
            MouseActionFlags flag = e.ChangedButton switch
            {
                MouseButton.Left => MouseActionFlags.LeftUp,
                MouseButton.Right => MouseActionFlags.RightUp,
                MouseButton.Middle => MouseActionFlags.MiddleUp,
                _ => MouseActionFlags.None
            };

            if (e.ChangedButton == MouseButton.Left) _isLeftDown = false;
            if (e.ChangedButton == MouseButton.Right) _isRightDown = false;
            if (e.ChangedButton == MouseButton.Middle) _isMiddleDown = false;

            if (flag != MouseActionFlags.None)
            {
                SendMouseIfAllowed(flag, e, isButtonEvent: true);
            }

            // 当所有按键都已松开时，释放鼠标捕获
            if (!_isLeftDown && !_isRightDown && !_isMiddleDown)
            {
                ImgDisplay.ReleaseMouseCapture();
            }
        }

        private void OnImageMouseWheel(object sender, MouseWheelEventArgs e)
        {
            SendMouseIfAllowed(MouseActionFlags.Wheel, e, e.Delta);
        }

        private void OnImageLostMouseCapture(object sender, MouseEventArgs e)
        {
            ReleaseStuckButtons();
        }

        protected override void OnDeactivated(EventArgs e)
        {
            base.OnDeactivated(e);
            ReleaseStuckButtons();
        }

        private void ReleaseStuckButtons()
        {
            if (_isLeftDown)
            {
                _isLeftDown = false;
                SendMouseRaw(MouseActionFlags.LeftUp, _lastNormX, _lastNormY);
            }
            if (_isRightDown)
            {
                _isRightDown = false;
                SendMouseRaw(MouseActionFlags.RightUp, _lastNormX, _lastNormY);
            }
            if (_isMiddleDown)
            {
                _isMiddleDown = false;
                SendMouseRaw(MouseActionFlags.MiddleUp, _lastNormX, _lastNormY);
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (!_session.CanControl || _session.InputService == null) return;

            ushort vk = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
            if (vk != 0)
            {
                var dto = new KeyEventDto
                {
                    VirtualKey = vk,
                    IsKeyDown = true,
                    IsExtendedKey = (e.Key == Key.RightAlt || e.Key == Key.RightCtrl)
                };
                Task.Run(() =>
                {
                    try { _session.InputService?.SendKeyEvent(dto); } catch { }
                });
                e.Handled = true;
            }
        }

        private void OnWindowKeyUp(object sender, KeyEventArgs e)
        {
            if (!_session.CanControl || _session.InputService == null) return;

            ushort vk = (ushort)KeyInterop.VirtualKeyFromKey(e.Key);
            if (vk != 0)
            {
                var dto = new KeyEventDto
                {
                    VirtualKey = vk,
                    IsKeyDown = false,
                    IsExtendedKey = (e.Key == Key.RightAlt || e.Key == Key.RightCtrl)
                };
                Task.Run(() =>
                {
                    try { _session.InputService?.SendKeyEvent(dto); } catch { }
                });
                e.Handled = true;
            }
        }

        #endregion

        #region 顶部工具栏操作

        private void OnRefreshKeyFrameClicked(object sender, RoutedEventArgs e)
        {
            _session.ScreenService?.RequestKeyFrame();
        }

        private void OnResetInputClicked(object sender, RoutedEventArgs e)
        {
            _session.InputService?.ResetInputState();
        }

        private void OnSendCadClicked(object sender, RoutedEventArgs e)
        {
            if (!_session.CanControl || _session.InputService == null) return;

            // 模拟发送 Ctrl + Alt + Del
            var cadSequence = new List<KeyEventDto>
            {
                new() { VirtualKey = 0x11, IsKeyDown = true }, // Ctrl down
                new() { VirtualKey = 0x12, IsKeyDown = true }, // Alt down
                new() { VirtualKey = 0x2E, IsKeyDown = true, IsExtendedKey = true }, // Del down
                new() { VirtualKey = 0x2E, IsKeyDown = false, IsExtendedKey = true }, // Del up
                new() { VirtualKey = 0x12, IsKeyDown = false }, // Alt up
                new() { VirtualKey = 0x11, IsKeyDown = false }  // Ctrl up
            };
            Task.Run(() =>
            {
                try { _session.InputService?.SendKeySequence(cadSequence); } catch { }
            });
        }

        private void OnToggleFullscreenClicked(object sender, RoutedEventArgs e)
        {
            _isFullscreen = !_isFullscreen;
            if (_isFullscreen)
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
                BtnFullscreen.Content = "退出全屏";
            }
            else
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
                BtnFullscreen.Content = "全屏";
            }
        }

        #endregion

        protected override void OnStateChanged(EventArgs e)
        {
            base.OnStateChanged(e);
            if (WindowState != WindowState.Minimized)
            {
                _framesReceivedSinceRefill = 0;
                Task.Run(() =>
                {
                    try { _session.ScreenService?.SetRefreshQuota(TargetQuota); } catch { }
                });
            }
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (WindowState != WindowState.Minimized)
            {
                Task.Run(() =>
                {
                    try { _session.ScreenService?.SetRefreshQuota(TargetQuota); } catch { }
                });
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _statsTimer.Stop();
            _session.FrameReceived -= OnFrameReceived;
            _session.InputService?.ResetInputState();
        }
    }
}
