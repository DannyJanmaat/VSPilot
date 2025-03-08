using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VSPilot.Common.Models;
using VSPilot.Core.Models;
using VSPilot.Core.Services;

namespace VSPilot.Core.Services
{
    /// <summary>
    /// Advanced task scheduling and management service for VSPilot automation tasks.
    /// </summary>
    public class TaskScheduler : IDisposable
    {
        /// <summary>
        /// Represents the priority levels for tasks.
        /// </summary>
        public enum TaskPriority
        {
            Low = 0,
            Normal = 1,
            High = 2,
            Critical = 3
        }

        /// <summary>
        /// Represents a scheduled task with additional metadata.
        /// </summary>
        private class ScheduledTask
        {
            public AutomationTask Task { get; set; } = null!;
            public TaskPriority Priority { get; set; }
            public CancellationTokenSource CancellationTokenSource { get; set; } = null!;
            public DateTime ScheduledTime { get; set; }
            public Guid Id { get; set; } = Guid.NewGuid();
        }

        private readonly ConcurrentPriorityQueue<ScheduledTask> _taskQueue;
        private readonly LoggingService _logger;
        private readonly CancellationTokenSource _schedulerCancellation;
        private Task? _processingTask;
        private bool _isProcessing;
        private bool _disposed;

        /// <summary>
        /// Event raised when task progress changes.
        /// </summary>
        public event EventHandler<ProgressInfo>? ProgressChanged;

        /// <summary>
        /// Event raised when a task is added to the queue.
        /// </summary>
        public event EventHandler<AutomationTask>? TaskQueued;

        /// <summary>
        /// Event raised when a task is completed.
        /// </summary>
        public event EventHandler<AutomationTask>? TaskCompleted;

        /// <summary>
        /// Event raised when a task is cancelled.
        /// </summary>
        public event EventHandler<AutomationTask>? TaskCancelled;

        /// <summary>
        /// Creates a new instance of the TaskScheduler.
        /// </summary>
        /// <param name="logger">The logging service for recording task events.</param>
        public TaskScheduler(LoggingService logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _taskQueue = new ConcurrentPriorityQueue<ScheduledTask>();
            _schedulerCancellation = new CancellationTokenSource();
        }

        /// <summary>
        /// Queues a new task for execution.
        /// </summary>
        /// <param name="task">The automation task to queue.</param>
        /// <param name="priority">The priority of the task (defaults to Normal).</param>
        /// <param name="scheduledTime">Optional scheduled time for the task.</param>
        /// <returns>The unique identifier of the queued task.</returns>
        public Guid QueueTask(
            AutomationTask task,
            TaskPriority priority = TaskPriority.Normal,
            DateTime? scheduledTime = null)
        {
            if (task == null)
            {
                throw new ArgumentNullException(nameof(task));
            }

            var scheduledTask = new ScheduledTask
            {
                Task = task,
                Priority = priority,
                ScheduledTime = scheduledTime ?? DateTime.Now,
                CancellationTokenSource = new CancellationTokenSource()
            };

            _taskQueue.Enqueue(scheduledTask, (int)priority);
            _logger.LogInformation($"Task queued: {task.Description} (Priority: {priority})");

            TaskQueued?.Invoke(this, task);

            // Start processing if not already running
            EnsureProcessing();

            return scheduledTask.Id;
        }

        /// <summary>
        /// Cancels a specific task by its identifier.
        /// </summary>
        /// <param name="taskId">The unique identifier of the task to cancel.</param>
        /// <returns>True if the task was successfully cancelled, false otherwise.</returns>
        public bool CancelTask(Guid taskId)
        {
            // Find and cancel the task in the queue
            var tasksToRemove = _taskQueue.UnorderedItems
                .Where(t => t.Id == taskId)
                .ToList();

            foreach (var task in tasksToRemove)
            {
                task.CancellationTokenSource.Cancel();
                _logger.LogInformation($"Task cancelled: {task.Task.Description}");
                TaskCancelled?.Invoke(this, task.Task);
            }

            return tasksToRemove.Any();
        }

        /// <summary>
        /// Ensures that task processing is started if not already running.
        /// </summary>
        private void EnsureProcessing()
        {
            if (!_isProcessing)
            {
                _isProcessing = true;
                _processingTask = ProcessQueueAsync();
            }
        }

        /// <summary>
        /// Processes tasks from the queue asynchronously.
        /// </summary>
        private async Task ProcessQueueAsync()
        {
            try
            {
                while (!_schedulerCancellation.Token.IsCancellationRequested)
                {
                    // Wait if no tasks are available
                    if (_taskQueue.Count == 0)
                    {
                        await Task.Delay(500, _schedulerCancellation.Token);
                        continue;
                    }

                    // Dequeue the next task
                    ScheduledTask scheduledTask;
                    if (!_taskQueue.TryDequeue(out scheduledTask))
                    {
                        continue;
                    }

                    // Check if task is scheduled for the future
                    if (scheduledTask.ScheduledTime > DateTime.Now)
                    {
                        var delay = scheduledTask.ScheduledTime - DateTime.Now;
                        await Task.Delay(delay, _schedulerCancellation.Token);
                    }

                    try
                    {
                        // Execute the task
                        await ExecuteTaskAsync(scheduledTask);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation($"Task cancelled: {scheduledTask.Task.Description}");
                        TaskCancelled?.Invoke(this, scheduledTask.Task);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Task execution failed: {scheduledTask.Task.Description}", ex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Task scheduler shutting down");
            }
            catch (Exception ex)
            {
                _logger.LogError("Unhandled error in task scheduler", ex);
            }
            finally
            {
                _isProcessing = false;
            }
        }

        /// <summary>
        /// Executes a single task with progress tracking.
        /// </summary>
        private async Task ExecuteTaskAsync(ScheduledTask scheduledTask)
        {
            try
            {
                await scheduledTask.Task.ExecuteAsync(progress =>
                {
                    // Raise progress event
                    ProgressChanged?.Invoke(this, progress);

                    // Check for cancellation
                    if (scheduledTask.CancellationTokenSource.Token.IsCancellationRequested)
                    {
                        throw new OperationCanceledException();
                    }
                });

                // Task completed successfully
                _logger.LogInformation($"Task completed: {scheduledTask.Task.Description}");
                TaskCompleted?.Invoke(this, scheduledTask.Task);
            }
            catch (OperationCanceledException)
            {
                throw; // Rethrow to be handled by caller
            }
            catch (Exception ex)
            {
                _logger.LogError($"Task failed: {scheduledTask.Task.Description}", ex);
                throw;
            }
        }

        /// <summary>
        /// Gets the current number of queued tasks.
        /// </summary>
        public int QueuedTaskCount => _taskQueue.Count;

        /// <summary>
        /// Stops the task scheduler and cancels all pending tasks.
        /// </summary>
        public void Stop()
        {
            _schedulerCancellation.Cancel();

            // Cancel all queued tasks
            while (_taskQueue.TryDequeue(out var task))
            {
                task.CancellationTokenSource.Cancel();
            }

            _logger.LogInformation("Task scheduler stopped");
        }

        /// <summary>
        /// Disposes of the task scheduler and its resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Handles disposal of managed and unmanaged resources.
        /// </summary>
        /// <param name="disposing">True if disposing managed resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    // Stop the scheduler
                    Stop();

                    // Dispose cancellation token source
                    _schedulerCancellation.Dispose();

                    // Dispose any other disposable resources
                }
                _disposed = true;
            }
        }

        /// <summary>
        /// Custom priority queue to support task prioritization.
        /// </summary>
        private class ConcurrentPriorityQueue<T>
        {
            private readonly ConcurrentDictionary<int, ConcurrentQueue<T>> _queues
                = new ConcurrentDictionary<int, ConcurrentQueue<T>>();

            public int Count => _queues.Sum(q => q.Value.Count);

            public void Enqueue(T item, int priority)
            {
                var queue = _queues.GetOrAdd(priority, p => new ConcurrentQueue<T>());
                queue.Enqueue(item);
            }

            public bool TryDequeue(out T item)
            {
                item = default!;

                // Find the highest priority non-empty queue
                var priorityQueues = _queues
                    .OrderByDescending(q => q.Key)
                    .ToList();

                foreach (var priorityQueue in priorityQueues)
                {
                    if (priorityQueue.Value.TryDequeue(out item))
                    {
                        return true;
                    }
                }

                return false;
            }

            public IEnumerable<T> UnorderedItems =>
                _queues.Values.SelectMany(q => q);
        }
    }
}
