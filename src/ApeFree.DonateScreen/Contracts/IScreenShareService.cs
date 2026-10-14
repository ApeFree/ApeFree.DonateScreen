using ApeFree.ApeRpc.Attributes;
using System;

namespace ApeFree.DonateScreen.Contracts
{
    /// <summary>
    /// 远程屏幕共享服务契约。
    /// 由 Slave 端实现并暴露，Master 端通过代理获取屏幕元数据并接收实时脏矩形帧流。
    /// </summary>
    public interface IScreenShareService
    {
        /// <summary>
        /// 屏幕增量脏矩形帧主动推送事件。
        /// 标记为强制自动订阅，在直连通道建立后，Slave 端采集循环主动向 Master 推送增量帧。
        /// </summary>
        [RpcEvent(IsForcedSubscription = true)]
        event EventHandler<ScreenFramePacketDto> FrameArrived;

        /// <summary>
        /// 屏幕拓扑与分辨率变动事件（如受控端分辨率切换、多显示器插拔）
        /// </summary>
        [RpcEvent(IsForcedSubscription = true)]
        event EventHandler<ScreenTopologyDto> TopologyChanged;

        /// <summary>
        /// 获取受控端当前显示器拓扑与分辨率信息
        /// </summary>
        [InvariableProperty]
        ScreenTopologyDto ScreenTopology { get; }

        /// <summary>
        /// 获取受控端当前显示器拓扑与分辨率信息
        /// </summary>
        ScreenTopologyDto GetScreenTopology();

        /// <summary>
        /// 获取受控端上线注册与分享配置信息（包含设备名、分辨率与分享模式）
        /// </summary>
        SlaveNodeRegistrationDto GetRegistrationInfo();

        /// <summary>
        /// 主动请求受控端立即生成并发送一个全屏完整关键帧（I-Frame/Full Refresh）
        /// 通常用于 Master 端刚刚接入、画面异常或用户手动点击“强制刷新”时调用。
        /// </summary>
        void RequestKeyFrame();

        /// <summary>
        /// 动态调整屏幕采集与编码参数
        /// </summary>
        /// <param name="options">编码与捕获配置（如最大 FPS、JPEG 质量 1~100、选中显示器索引）</param>
        void UpdateCaptureOptions(CaptureOptionsDto options);

        /// <summary>
        /// 暂停或恢复屏幕帧的主动推送
        /// </summary>
        /// <param name="isPaused">是否暂停推流</param>
        void SetStreamingPaused(bool isPaused);

        /// <summary>
        /// 增加或设置屏幕画面刷新配额（仅当 newQuota 大于当前剩余配额时生效，保证流控单调递增并清除过期积压）。
        /// 远程设置了次数为 n 后，计数会变成 n，每次刷新计数都会 -1，到 0 后不再触发屏幕刷新。
        /// </summary>
        /// <param name="newQuota">新的刷新帧数配额</param>
        void SetRefreshQuota(int newQuota);
    }
}
