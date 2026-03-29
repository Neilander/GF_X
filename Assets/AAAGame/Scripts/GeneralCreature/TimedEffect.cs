using System.Collections.Generic;

public class TimedEffect
{
     public float duration;

     public TimedEffect(float duration)
     {
          this.duration = duration;
     }

     public virtual bool UpdateAndCheck(float deltaTime)
     {
          duration -= deltaTime;
          return duration <= 0;
     }
}