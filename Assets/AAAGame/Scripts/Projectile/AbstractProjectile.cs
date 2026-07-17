using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class AbstractProjectile : EntityBase
{
   private const string HitBoxEntityAssetName = "HitBox";

   // ==== 方向类的 ==== 
   /* 路径类很简单，现在只要给一个方向，自己有一个速度。
    * 然后记录从什么阶段开始打开hitbox，什么阶段关闭hitbox
    *
    * 其中owner和damage 都是靠 外部赋值，然后转发给hitbox
    *
    * 之后如果出现特殊方向移动，就覆写move
    */ 
   
   // ==== 追踪类的 ==== 
   /* 追踪类需要提前传入target，其他类似的方向类
    * 要有个属性，是否allow block，类似诸葛大招
    *
    * 特殊移动也是覆写move
    *
    * 有个核心特征是会维护一个targetPosition。在目标活着的时候就是目标位置
    * 目标死了就不更新，朝当前目标位置移动
    */
   
   // ==== 预设轨道类的 ==== 
   /* 这种用于对轨道有很明确需求的子弹，例如皎月的技能
    * 移动不再是自己计算，而是要传入一个轨道。自己维护一个时间T
    * 用计算得到的percent给轨道，得到当前应该在的位置和转向
    *
    */
   protected ProjectileConfig config;
   protected bool startMove = false;
   
   protected float elapsedTime = 0;
   protected HitBox hitBox;

   protected override void OnInit(object userData)
   {
       base.OnInit(userData);
       config = GetComponent<ProjectileConfig>();
   }

   protected override void OnShow(object userData)
   {
       base.OnShow(userData);
   }

   protected override void OnLogicFrameUpdate(Fix64 deltaTime)
   {
       base.OnLogicFrameUpdate(deltaTime);

       if (!startMove)
           return;

       float deltaSeconds = (float)deltaTime;
      //GF.Log("lassds");
       elapsedTime += deltaSeconds;
       Move(elapsedTime, deltaSeconds);
   }
   
   protected abstract void Move(float totalPassed, float deltaTime);
   
   public abstract void DestroyProjectile();

   protected void StartMove()
   {
       
       //这是通用的开始设置，用于给继承者调用
       startMove = true;
       elapsedTime = 0;
       
       var hitboxParams = EntityParams.Create();
       hitboxParams.AttchToEntity = Entity;
       hitboxParams.localScale = config.hitboxScale;

       hitboxParams.OnShowCallback = logic =>
       {
            hitBox = (HitBox)logic;
            logic.transform.localPosition = config.hitboxOffset;
       };
       GF.Entity.ShowEntity<HitBox>(HitBoxEntityAssetName, Const.EntityGroup.Default, hitboxParams);
       //这里还没开启hitbox

   }

   protected override void OnHide(bool isShutdown, object userData)
   {
       base.OnHide(isShutdown, userData);
       startMove = false;
   }
}
