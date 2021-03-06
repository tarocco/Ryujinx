﻿using Ryujinx.Common;
using Ryujinx.Common.Logging;

namespace Ryujinx.HLE.HOS.Services.Bcat.ServiceCreator
{
    // TODO: implementation
    class IDeliveryCacheDirectoryService : IpcService
    {
        [Command(0)]
        public ResultCode Open(ServiceCtx context)
        {
            Logger.PrintStub(LogClass.ServiceBcat);
            return ResultCode.Success;
        }

        [Command(1)]
        public ResultCode Read(ServiceCtx context)
        {
            Logger.PrintStub(LogClass.ServiceBcat);
            return ResultCode.Success;
        }

        [Command(2)]
        public ResultCode GetCount(ServiceCtx context)
        {
            Logger.PrintStub(LogClass.ServiceBcat);
            return ResultCode.Success;
        }
    }
}
