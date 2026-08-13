
public interface ICapability
{
    
    /*
     * Capability结构中，是否调用某个能力是由Entity决定的，如当前我是否可以行走，是否可以普攻
     * 这里的ShutDown和Resume是处理一些自己的事，比如说普攻有个特效，ShutDown需要关闭这种
     */
    void ShutDown();
    void Resume();
}
