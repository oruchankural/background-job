using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Quartz;
using Quartz.Impl;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

// Configure and start the Quartz scheduler as a singleton service
builder.Services.AddSingleton(provider =>
{
    var httpContextAccessor = provider.GetRequiredService<IHttpContextAccessor>();
    var request = httpContextAccessor.HttpContext?.Request;
    var baseUrl = $"{request?.Scheme}://{request?.Host}";

    var schedulerFactory = new StdSchedulerFactory();
    var scheduler = schedulerFactory.GetScheduler().Result;
    scheduler.Start().Wait();

    return scheduler;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapPost("/api/data", async context =>
{
    var scheduler = context.RequestServices.GetRequiredService<IScheduler>();
    var dataList = await context.Request.ReadFromJsonAsync<List<Data>>();

    int batchSize = 100;
    var totalDataCount = dataList.Count;
    var jobCount = (int)Math.Ceiling((double)totalDataCount / batchSize);

    for (int i = 0; i < jobCount; i++)
    {
        var batchData = dataList.Skip(i * batchSize).Take(batchSize).ToList();

        var jobDataMap = new JobDataMap
        {
            { "BatchData", batchData }
        };

        var jobKey = new JobKey($"myJob_{i + 1}", "myGroup"); 

        var jobDetail = JobBuilder.Create<InsertLogToDatabase>()
            .WithIdentity(jobKey)
            .UsingJobData(jobDataMap)
            .Build();

       // Start each job at different times, 2 minutes apart
        var startTime = DateTime.Now.AddMinutes(2 * i);

        var trigger = TriggerBuilder.Create()
            .WithIdentity(jobKey.Name, jobKey.Group)
            .StartAt(startTime)
            .WithSimpleSchedule(x => x
                .WithIntervalInMinutes(1))
            .Build();

        await scheduler.ScheduleJob(jobDetail, trigger);
    }

    context.Response.StatusCode = 200;
    await context.Response.WriteAsync("Jobs started.");
});

app.Run();

[DisallowConcurrentExecution]
public class InsertLogToDatabase : IJob
{
    public Task Execute(IJobExecutionContext context)
    {
        var jobData = context.JobDetail.JobDataMap;
        if (jobData.ContainsKey("BatchData") && jobData["BatchData"] is List<Data> batchData)
        {
            foreach (var data in batchData)
            {
                string logMessage = $"Data processed for job: {data.Id} - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
                System.Console.WriteLine(logMessage);
            }
        }
        return Task.CompletedTask;
    }
}
// TODO: make it more generic
public class Data
{
    public int Id { get; set; }
    public string Message { get; set; }
}
