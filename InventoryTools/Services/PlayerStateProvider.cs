using Dalamud.IoC;
using Dalamud.Plugin.Services;

namespace InventoryTools.Services
{
    /// <summary>
    /// API13 把 <see cref="IClientState.LocalContentId"/> 標為過時，替代品是
    /// <see cref="IPlayerState.ContentId"/>（Dalamud 端本來就是
    /// <c>LocalContentId =&gt; playerState.ContentId</c> 的純轉發）。
    /// <para>
    /// CriticalCommonLib.Services.CharacterMonitor 需要 <see cref="IPlayerState"/>，但
    /// DalaMock 的 HostedPlugin 只把「明確傳進 base(...) 的服務清單」註冊進 Autofac 容器，
    /// 而那份清單沒有 IPlayerState；DalaMock.Core 也沒有對應的 mock 服務，因此無法沿用
    /// 一般的建構子注入串接（那會讓 InventoryToolsMock 的 dev launcher 拿不到而壞掉）。
    /// </para>
    /// <para>
    /// 這裡改用 <see cref="Dalamud.Plugin.IDalamudPluginInterface.Create{T}"/> 讓 Dalamud
    /// 以 <see cref="PluginServiceAttribute"/> 注入 IPlayerState，再由 InventoryToolsPlugin
    /// 於 ConfigureContainer 把實例註冊進容器。Mock 模式下 CharacterMonitor 會被
    /// MockCharacterMonitor 取代、不會解析 IPlayerState，故 Create 取不到時安全略過。
    /// </para>
    /// </summary>
    internal class PlayerStateProvider
    {
        [PluginService] public IPlayerState PlayerState { get; private set; } = null!;
    }
}
