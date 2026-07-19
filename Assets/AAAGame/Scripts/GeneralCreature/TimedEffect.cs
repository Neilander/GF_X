using System.Collections.Generic;

public class TimedEffect
{
     public Fix64 duration;

     public TimedEffect(float duration)
         : this((Fix64)duration)
     {
     }

     public TimedEffect(Fix64 duration)
     {
          this.duration = duration;
     }

     public virtual bool UpdateAndCheck(Fix64 deltaTime)
     {
          duration -= deltaTime;
          return duration <= Fix64.Zero;
     }
}
