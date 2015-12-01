﻿using System;
using UniRx.Operators;

namespace UniRx.Operators
{
    internal class Take<T> : OperatorObservableBase<T>
    {
        readonly IObservable<T> source;
        readonly int count;
        readonly TimeSpan duration;
        internal readonly IScheduler scheduler; // public for optimization check

        public Take(IObservable<T> source, int count)
            : base(source.IsRequiredSubscribeOnCurrentThread())
        {
            this.source = source;
            this.count = count;
        }

        public Take(IObservable<T> source, TimeSpan duration, IScheduler scheduler)
            : base(scheduler == Scheduler.CurrentThread || source.IsRequiredSubscribeOnCurrentThread())
        {
            this.source = source;
            this.duration = duration;
            this.scheduler = scheduler;
        }

        // optimize combiner

        public IObservable<T> Combine(int count)
        {
            // xs = 6
            // xs.Take(5) = 5         | xs.Take(3) = 3
            // xs.Take(5).Take(3) = 3 | xs.Take(3).Take(5) = 3

            // use minimum one
            return (this.count <= count)
                ? this
                : new Take<T>(source, count);
        }

        public IObservable<T> Combine(TimeSpan duration)
        {
            // xs = 6s
            // xs.Take(5s) = 5s          | xs.Take(3s) = 3s
            // xs.Take(5s).Take(3s) = 3s | xs.Take(3s).Take(5s) = 3s

            // use minimum one
            return (this.duration <= duration)
                ? this
                : new Take<T>(source, duration, scheduler);
        }

        protected override IDisposable SubscribeCore(IObserver<T> observer, IDisposable cancel)
        {
            if (scheduler == null)
            {
                return source.Subscribe(new TakeObserver(this, observer, cancel));
            }
            else
            {
                return new TakeDurationObserver(this, observer, cancel).Run();
            }
        }

        class TakeObserver : OperatorObserverBase<T, T>
        {
            readonly Take<T> parent;
            int rest;

            public TakeObserver(Take<T> parent, IObserver<T> observer, IDisposable cancel) : base(observer, cancel)
            {
                this.parent = parent;
                this.rest = parent.count;
            }

            public override void OnNext(T value)
            {
                if (rest > 0)
                {
                    rest -= 1;
                    base.observer.OnNext(value);
                    if (rest == 0)
                    {
                        base.OnCompleted();
                    }
                }
            }
        }

        class TakeDurationObserver : OperatorObserverBase<T, T>
        {
            readonly Take<T> parent;
            readonly object gate = new object();

            public TakeDurationObserver(Take<T> parent, IObserver<T> observer, IDisposable cancel) : base(observer, cancel)
            {
                this.parent = parent;
            }

            public IDisposable Run()
            {
                var d1 = parent.scheduler.Schedule(parent.duration, Tick);
                var d2 = parent.source.Subscribe(this);

                return StableCompositeDisposable.Create(d1, d2);
            }

            void Tick()
            {
                lock (gate)
                {
                    base.OnCompleted();
                }
            }

            public override void OnNext(T value)
            {
                lock (gate)
                {
                    base.observer.OnNext(value);
                }
            }

            public override void OnError(Exception error)
            {
                lock (gate)
                {
                    base.OnError(error);
                }
            }

            public override void OnCompleted()
            {
                lock (gate)
                {
                    base.OnCompleted();
                }
            }
        }
    }
}