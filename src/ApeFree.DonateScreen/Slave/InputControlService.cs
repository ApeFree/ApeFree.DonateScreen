using ApeFree.DonateScreen.Contracts;
using ApeFree.DonateScreen.Input;
using System;
using System.Collections.Generic;

namespace ApeFree.DonateScreen.Slave
{
    /// <summary>
    /// 远程键盘鼠标输入控制服务实现（由 Slave 节点暴露）
    /// </summary>
    public class InputControlService : IInputControlService
    {
        private bool _isEnabled = true;

        /// <summary>
        /// 是否允许接收并执行远程输入控制（受控端本地可一键停用）
        /// </summary>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                if (!value)
                {
                    ResetInputState();
                }
            }
        }

        /// <summary>
        /// 收到远端鼠标动作
        /// </summary>
        public void SendMouseEvent(MouseEventDto dto)
        {
            if (!_isEnabled || dto == null) return;
            Win32InputSimulator.SendMouse(dto);
        }

        /// <summary>
        /// 收到远端键盘动作
        /// </summary>
        public void SendKeyEvent(KeyEventDto dto)
        {
            if (!_isEnabled || dto == null) return;
            Win32InputSimulator.SendKey(dto);
        }

        /// <summary>
        /// 批量执行键盘动作
        /// </summary>
        public void SendKeySequence(List<KeyEventDto> events)
        {
            if (!_isEnabled || events == null) return;
            Win32InputSimulator.SendKeySequence(events);
        }

        /// <summary>
        /// 紧急复位按键
        /// </summary>
        public void ResetInputState()
        {
            Win32InputSimulator.ResetKeyStates();
        }
    }
}
