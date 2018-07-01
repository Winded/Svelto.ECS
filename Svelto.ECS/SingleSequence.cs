using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Svelto.ECS
{
    public interface ISingleSequence<T>
    {
        void Trigger(ref T token);
        void Trigger(ref T token, int condition);
    }

    public class SingleSequence<T> : ISingleSequence<T>
    {
        private readonly Dictionary<int, List<IStep<T>>> steps = new Dictionary<int, List<IStep<T>>>();

        private int currentCondition = 0;

        public SingleSequence<T> Next(IStep<T> step)
        {
            List<IStep<T>> conditionSteps;
            if (!steps.TryGetValue(currentCondition, out conditionSteps))
            {
                conditionSteps = new List<IStep<T>>();
                steps.Add(currentCondition, conditionSteps);
            }

            conditionSteps.Add(step);

            return this;
        }

        public SingleSequence<T> Condition(int newCondition)
        {
            currentCondition = newCondition;
            return this;
        }

        public void Trigger(ref T token)
        {
            Trigger(ref token, 0);
        }

        public void Trigger(ref T token, int condition)
        {
            List<IStep<T>> conditionSteps;
            if(!steps.TryGetValue(condition, out conditionSteps))
            {
                return;
            }

            foreach(var step in conditionSteps)
            {
                step.Step(ref token, condition);
            }
        }
    }
}
