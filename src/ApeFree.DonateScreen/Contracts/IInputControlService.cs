using System.Collections.Generic;

namespace ApeFree.DonateScreen.Contracts
{
    /// <summary>
    /// 远程键盘鼠标输入控制服务契约。
    /// 由 Slave 端根据分享模式选择性暴露；Master 端通过代理将用户在显示视窗上的操作注入受控机。
    /// </summary>
    public interface IInputControlService
    {
        /// <summary>
        /// 发送鼠标绝对位置移动、按键与滚轮事件
        /// </summary>
        /// <param name="dto">鼠标动作数据包（归一化坐标、滚轮增量、按键动作）</param>
        void SendMouseEvent(MouseEventDto dto);

        /// <summary>
        /// 发送键盘按键事件（按下/释放）
        /// </summary>
        /// <param name="dto">键盘动作数据包（虚拟键码、硬件扫描码、扩展键标志、按键状态）</param>
        void SendKeyEvent(KeyEventDto dto);

        /// <summary>
        /// 批量发送按键动作（如发送字符串文本或模拟快捷键宏）
        /// </summary>
        /// <param name="events">按键序列</param>
        void SendKeySequence(List<KeyEventDto> events);

        /// <summary>
        /// 紧急复位受控机所有按键状态（释放所有被按下的 Ctrl/Shift/Alt/Win 等按键，防止键位粘滞）
        /// </summary>
        void ResetInputState();
    }
}
