using CaoMinhComputer.Core.Services;
using CaoMinhComputer.Service;
using CaoMinhComputer.Service.Ipc;
using CaoMinhComputer.Service.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Microsoft.Extensions.Logging;

if (args.Contains("--remove-applocker"))
{
    // Chế độ dòng lệnh (bộ gỡ cài đặt dùng qua tác vụ SYSTEM khi dịch vụ đã dừng): gỡ nhóm quy tắc AppLocker của Con Ngoan.
    var removed = AppLockerManager.Apply([], [], false, []);
    return removed.Success ? 0 : 1;
}

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWindowsService(options => options.ServiceName = AppPaths.ServiceName);
if (WindowsServiceHelpers.IsWindowsService())
{
    // Thay vòng đời mặc định để nhận sự kiện đăng nhập/khóa/mở khóa phiên và sleep/resume.
    builder.Services.AddSingleton<IHostLifetime, CmcServiceLifetime>();
}
builder.Logging.ClearProviders();
// Chỉ ghi cảnh báo/lỗi kỹ thuật vào Windows Event Log; nhật ký nghiệp vụ nằm trong SQLite.
builder.Logging.AddEventLog(settings => settings.SourceName = "ConNgoan");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddHostedService<ProtectionWorker>();
builder.Services.AddHostedService<TimeWorker>();
builder.Services.AddHostedService<PipeServerWorker>();
builder.Services.AddHostedService<AgentPipeWorker>();
builder.Services.AddHostedService<UpdateWorker>();
builder.Services.AddHostedService<TelegramWorker>();

var host = builder.Build();
host.Run();
return 0;
