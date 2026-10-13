using ApeFree.DonateScreen.Contracts;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ApeFree.DonateScreen.Input
{
    /// <summary>
    /// 基于 Win32 SendInput API 的底层键盘与鼠标输入模拟执行器
    /// </summary>
    public static class Win32InputSimulator
    {
        private const int INPUT_MOUSE = 0;
        private const int INPUT_KEYBOARD = 1;

        private const uint MOUSEEVENTF_MOVE = 0x0001;
        private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        private const uint MOUSEEVENTF_LEFTUP = 0x0004;
        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
        private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
        private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
        private const uint MOUSEEVENTF_WHEEL = 0x0800;
        private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
        private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_SCANCODE = 0x0008;

        [StructLayout(LayoutKind.Sequential)]
        private struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public int mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct INPUT
        {
            [FieldOffset(0)]
            public int type;
            [FieldOffset(8)]
            public MOUSEINPUT mi;
            [FieldOffset(8)]
            public KEYBDINPUT ki;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint nInputs, [In] INPUT[] pInputs, int cbSize);

        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);

        [DllImport("user32.dll")]
        private static extern uint MapVirtualKey(uint uCode, uint uMapType);

        /// <summary>
        /// 注入鼠标事件
        /// </summary>
        public static void SendMouse(MouseEventDto dto)
        {
            if (dto == null) return;

            uint flags = 0;
            if (dto.Flags.HasFlag(MouseActionFlags.Move)) flags |= MOUSEEVENTF_MOVE;
            if (dto.Flags.HasFlag(MouseActionFlags.LeftDown)) flags |= MOUSEEVENTF_LEFTDOWN;
            if (dto.Flags.HasFlag(MouseActionFlags.LeftUp)) flags |= MOUSEEVENTF_LEFTUP;
            if (dto.Flags.HasFlag(MouseActionFlags.RightDown)) flags |= MOUSEEVENTF_RIGHTDOWN;
            if (dto.Flags.HasFlag(MouseActionFlags.RightUp)) flags |= MOUSEEVENTF_RIGHTUP;
            if (dto.Flags.HasFlag(MouseActionFlags.MiddleDown)) flags |= MOUSEEVENTF_MIDDLEDOWN;
            if (dto.Flags.HasFlag(MouseActionFlags.MiddleUp)) flags |= MOUSEEVENTF_MIDDLEUP;
            if (dto.Flags.HasFlag(MouseActionFlags.Wheel)) flags |= MOUSEEVENTF_WHEEL;

            // 无论是否有 Move，只要包含绝对坐标转换标志，均按绝对坐标对齐
            flags |= (MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK);

            var input = new INPUT
            {
                type = INPUT_MOUSE,
                mi = new MOUSEINPUT
                {
                    dx = dto.NormalizedX,
                    dy = dto.NormalizedY,
                    mouseData = dto.WheelDelta,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        /// <summary>
        /// 注入单个键盘按键事件
        /// </summary>
        public static void SendKey(KeyEventDto dto)
        {
            if (dto == null) return;

            uint dwFlags = 0;
            if (!dto.IsKeyDown)
            {
                dwFlags |= KEYEVENTF_KEYUP;
            }

            if (dto.IsExtendedKey)
            {
                dwFlags |= KEYEVENTF_EXTENDEDKEY;
            }

            ushort scanCode = dto.ScanCode;
            if (scanCode == 0 && dto.VirtualKey != 0)
            {
                scanCode = (ushort)MapVirtualKey(dto.VirtualKey, 0);
            }

            var input = new INPUT
            {
                type = INPUT_KEYBOARD,
                ki = new KEYBDINPUT
                {
                    wVk = dto.VirtualKey,
                    wScan = scanCode,
                    dwFlags = dwFlags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero
                }
            };

            SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
        }

        /// <summary>
        /// 批量注入键盘事件序列
        /// </summary>
        public static void SendKeySequence(IEnumerable<KeyEventDto> events)
        {
            if (events == null) return;
            var list = new List<INPUT>();

            foreach (var dto in events)
            {
                uint dwFlags = 0;
                if (!dto.IsKeyDown) dwFlags |= KEYEVENTF_KEYUP;
                if (dto.IsExtendedKey) dwFlags |= KEYEVENTF_EXTENDEDKEY;

                ushort scan = dto.ScanCode;
                if (scan == 0 && dto.VirtualKey != 0)
                {
                    scan = (ushort)MapVirtualKey(dto.VirtualKey, 0);
                }

                list.Add(new INPUT
                {
                    type = INPUT_KEYBOARD,
                    ki = new KEYBDINPUT
                    {
                        wVk = dto.VirtualKey,
                        wScan = scan,
                        dwFlags = dwFlags,
                        time = 0,
                        dwExtraInfo = IntPtr.Zero
                    }
                });
            }

            if (list.Count > 0)
            {
                SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
            }
        }

        /// <summary>
        /// 紧急复位所有关键修饰键（释放 Ctrl, Shift, Alt, Win 状态）
        /// </summary>
        public static void ResetKeyStates()
        {
            int[] modifierKeys = new int[]
            {
                0x10, // VK_SHIFT
                0x11, // VK_CONTROL
                0x12, // VK_MENU (ALT)
                0x5B, // VK_LWIN
                0x5C, // VK_RWIN
                0xA0, // VK_LSHIFT
                0xA1, // VK_RSHIFT
                0xA2, // VK_LCONTROL
                0xA3, // VK_RCONTROL
                0xA4, // VK_LMENU
                0xA5  // VK_RMENU
            };

            var releaseInputs = new List<INPUT>();

            foreach (var vk in modifierKeys)
            {
                short state = GetAsyncKeyState(vk);
                // 高位为 1 表示当前处于按下状态
                if ((state & 0x8000) != 0)
                {
                    releaseInputs.Add(new INPUT
                    {
                        type = INPUT_KEYBOARD,
                        ki = new KEYBDINPUT
                        {
                            wVk = (ushort)vk,
                            wScan = (ushort)MapVirtualKey((uint)vk, 0),
                            dwFlags = KEYEVENTF_KEYUP,
                            time = 0,
                            dwExtraInfo = IntPtr.Zero
                        }
                    });
                }
            }

            if (releaseInputs.Count > 0)
            {
                SendInput((uint)releaseInputs.Count, releaseInputs.ToArray(), Marshal.SizeOf<INPUT>());
            }
        }
    }
}
