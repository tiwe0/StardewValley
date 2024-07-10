using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace StardewValley.ConsoleAsync
{
	public class AsyncOperationManager
	{
		private static AsyncOperationManager _instance;

		private List<IAsyncOperation> _pendingOps;

		private List<IAsyncOperation> _tempOps;

		private List<IAsyncOperation> _doneOps;

		public static AsyncOperationManager Use => _instance;

		public static void Init()
		{
			_instance = new AsyncOperationManager();
		}

		private AsyncOperationManager()
		{
			_pendingOps = new List<IAsyncOperation>();
			_tempOps = new List<IAsyncOperation>();
			_doneOps = new List<IAsyncOperation>();
		}

		public void AddPending(Task task, Action<GenericResult> doneAction)
		{
			_003C_003Ec__DisplayClass8_0 _003C_003Ec__DisplayClass8_ = new _003C_003Ec__DisplayClass8_0();
			_003C_003Ec__DisplayClass8_.doneAction = doneAction;
			_003C_003Ec__DisplayClass8_.op = new GenericOp();
			_003C_003Ec__DisplayClass8_.op.DoneCallback = _003C_003Ec__DisplayClass8_._003CAddPending_003Eg__OnDone_007C0;
			_003C_003Ec__DisplayClass8_.op.Task = task;
			if (task.Status > TaskStatus.Created)
			{
				_003C_003Ec__DisplayClass8_.op.TaskStarted = true;
			}
			AddPending(_003C_003Ec__DisplayClass8_.op);
		}

		public void AddPending(Action workAction, Action<GenericResult> doneAction)
		{
			_003C_003Ec__DisplayClass9_0 _003C_003Ec__DisplayClass9_ = new _003C_003Ec__DisplayClass9_0();
			_003C_003Ec__DisplayClass9_.doneAction = doneAction;
			_003C_003Ec__DisplayClass9_.op = new GenericOp();
			_003C_003Ec__DisplayClass9_.op.DoneCallback = _003C_003Ec__DisplayClass9_._003CAddPending_003Eg__OnDone_007C0;
			Task task = new Task(workAction);
			_003C_003Ec__DisplayClass9_.op.Task = task;
			AddPending(_003C_003Ec__DisplayClass9_.op);
		}

		public void AddPending(IAsyncOperation op)
		{
			lock (_pendingOps)
			{
				_pendingOps.Add(op);
			}
		}

		public void Update()
		{
			lock (_pendingOps)
			{
				_doneOps.Clear();
				_tempOps.Clear();
				_tempOps.AddRange(_pendingOps);
				_pendingOps.Clear();
				bool working = false;
				for (int j = 0; j < _tempOps.Count; j++)
				{
					IAsyncOperation op = _tempOps[j];
					if (working)
					{
						_pendingOps.Add(op);
						continue;
					}
					working = true;
					if (!op.Started)
					{
						op.Begin();
						_pendingOps.Add(op);
					}
					else if (op.Done)
					{
						_doneOps.Add(op);
					}
					else
					{
						_pendingOps.Add(op);
					}
				}
				_tempOps.Clear();
			}
			for (int i = 0; i < _doneOps.Count; i++)
			{
				_doneOps[i].Conclude();
			}
			_doneOps.Clear();
		}
	}
}
