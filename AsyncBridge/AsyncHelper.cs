﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace AsyncBridge
{
    /// <summary>
    /// A Helper class to run Asynchronous functions from synchronous ones
    /// </summary>
    public static class AsyncHelper
    {
        /// <summary>
        /// A class to bridge synchronous asynchronous methods
        /// </summary>
        public class AsyncBridge : IDisposable
        {
            private ExclusiveSynchronizationContext CurrentContext;
            private SynchronizationContext OldContext;
            private int TaskCount;

            /// <summary>
            /// Constructs the AsyncBridge by capturing the current
            /// SynchronizationContext and replacing it with a new
            /// ExclusiveSynchronizationContext.
            /// </summary>
            internal AsyncBridge()
            {
                OldContext = SynchronizationContext.Current;
                CurrentContext = new ExclusiveSynchronizationContext();
                SynchronizationContext.SetSynchronizationContext(CurrentContext);
            }

            /// <summary>
            /// Execute's an async task with a void return type
            /// from a synchronous context
            /// </summary>
            /// <param name="task">Task to execute</param>
            /// <param name="callback">Optional callback</param>
            public void Run(Task task, Action<Task> callback = null)
            {
                if (null != callback)
                {
                    task.ContinueWith(callback);
                }
                CurrentContext.Post(async _ =>
                {
                    try
                    {
                        Increment();
                        await task;
                    }
                    catch (Exception e)
                    {
                        CurrentContext.InnerException = e;
                        throw;
                    }
                    finally
                    {
                        Decrement();
                    }
                }, null);
            }

            /// <summary>
            /// Execute's an async task with a T return type
            /// from a synchronous context
            /// </summary>
            /// <typeparam name="T">The type of the task</typeparam>
            /// <param name="task">Task to execute</param>
            /// <param name="callback">Optional callback</param>
            public void Run<T>(Task<T> task, Action<Task<T>> callback = null)
            {
                if (null != callback)
                {
                    task.ContinueWith(callback);
                }
                T ret = default(T);
                CurrentContext.Post(async _ =>
                {
                    try
                    {
                        Increment();
                        ret = await task;
                    }
                    catch (Exception e)
                    {
                        CurrentContext.InnerException = e;
                        throw;
                    }
                    finally
                    {
                        Decrement();
                    }
                }, null);
            }

            private void Increment()
            {
                Interlocked.Increment(ref TaskCount);
            }

            private void Decrement()
            {
                Interlocked.Decrement(ref TaskCount);
                if (TaskCount == 0)
                {
                    CurrentContext.EndMessageLoop();
                }
            }

            /// <summary>
            /// Disposes the object
            /// </summary>
            public void Dispose()
            {
                try
                {
                    CurrentContext.BeginMessageLoop();
                }
                catch (Exception e)
                {
                    CurrentContext.InnerException = e;
                    throw;
                }
                finally
                {
                    SynchronizationContext.SetSynchronizationContext(OldContext);
                }
            }
        }

        /// <summary>
        /// Creates a new AsyncBridge. This should always be used in
        /// conjunction with the using statement, to ensure it is disposed
        /// </summary>
        public static AsyncBridge Wait
        {
            get { return new AsyncBridge(); }
        }

        private class ExclusiveSynchronizationContext : SynchronizationContext
        {
            private bool done;
            public Exception InnerException { get; set; }
            readonly AutoResetEvent workItemsWaiting = new AutoResetEvent(false);
            readonly Queue<Tuple<SendOrPostCallback, object>> items =
                new Queue<Tuple<SendOrPostCallback, object>>();

            public override void Send(SendOrPostCallback d, object state)
            {
                throw new NotSupportedException("We cannot send to our same thread");
            }

            public override void Post(SendOrPostCallback d, object state)
            {
                lock (items)
                {
                    items.Enqueue(Tuple.Create(d, state));
                }
                workItemsWaiting.Set();
            }

            public void EndMessageLoop()
            {
                Post(_ => done = true, null);
            }

            public void BeginMessageLoop()
            {
                while (!done)
                {
                    Tuple<SendOrPostCallback, object> task = null;
                    lock (items)
                    {
                        if (items.Count > 0)
                        {
                            task = items.Dequeue();
                        }
                    }
                    if (task != null)
                    {
                        task.Item1(task.Item2);
                        if (InnerException != null) // the method threw an exeption
                        {
                            throw new AggregateException("AsyncHelpers.Run method threw an exception.", InnerException);
                        }
                    }
                    else
                    {
                        workItemsWaiting.WaitOne();
                    }
                }
            }

            public override SynchronizationContext CreateCopy()
            {
                return this;
            }
        }
    }
}