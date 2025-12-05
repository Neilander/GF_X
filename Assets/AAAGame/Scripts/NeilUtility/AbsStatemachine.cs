using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BaseUtility
{
    public abstract class AbsStatemachine<T1,T2>
    {

        protected T2 father;

        public T1 curState { get; private set; }

        public AbsStatemachine(T2 father)
        {
            this.father = father;
        }

        public void StartState(T1 newState)
        {
            EndState(curState);
            SwitchWhenStart(newState);
            curState = newState;
        }

        private void EndState(T1 lastState)
        {
            SwitchWhenEnd(lastState);
        }

        public void UpdateState()
        {
            SwitchWhenUpdate(curState);
        }

        public virtual void SwitchWhenStart(T1 newState)
        {

        }

        public virtual void SwitchWhenEnd(T1 lastState)
        {

        }
        
        public virtual void SwitchWhenUpdate(T1 curState)
        {
            
        }
    }
}

