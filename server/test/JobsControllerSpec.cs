/* Copyright (c) 2026, SeedTactics

All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted
provided that the following conditions are met:

    * Redistributions of source code must retain the above copyright notice, this list of
    conditions and the following disclaimer.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR
IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
 */

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BlackMaple.MachineFramework;
using BlackMaple.MachineFramework.Controllers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Shouldly;

namespace BlackMaple.FMSInsight.Tests;

public sealed class JobsControllerSpec
{
  [Test]
  public void CancelLoadForwardsTheOperationAndOperatorContext()
  {
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    var controller = new JobsController(null!, jobAndQueue);

    controller.CancelLoad(
      materialId: 17,
      request: new CancelLoadRequest()
      {
        ExpectedLoadCancellationId = "operation-1",
        Reason = "wrong fixture",
      },
      operName: "operator"
    );

    jobAndQueue.Received(1).CancelLoad(17, "operation-1", "operator", "wrong fixture");
  }

  [Test]
  public void SignalMaterialForQuarantineForwardsOperatorAndReason()
  {
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    var controller = new JobsController(null!, jobAndQueue);

    controller.SignalMaterialForQuarantine(17, "operator", "wrong fixture");

    jobAndQueue.Received(1).SignalMaterialForQuarantine(17, "operator", "wrong fixture");
  }

  [Test]
  public void QuarantineQueuedMaterialForwardsOperatorAndReason()
  {
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    var controller = new JobsController(null!, jobAndQueue);

    controller.QuarantineQueuedMaterial(17, "operator", "wrong fixture");

    jobAndQueue.Received(1).QuarantineQueuedMaterial(17, "operator", "wrong fixture");
  }

  [Test]
  public void InvalidationReturnsTheBackendMaterialDetails()
  {
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    var details = new MaterialDetails()
    {
      MaterialID = 17,
      PartName = "part",
      NumProcesses = 2,
    };
    jobAndQueue.InvalidatePalletCycle(17, 1, "operator", "new-casting", null).Returns(details);
    var controller = new JobsController(null!, jobAndQueue);

    var result = controller.InvalidatePalletCycle(
      materialId: 17,
      process: 1,
      operName: "operator",
      changeCastingTo: "new-casting",
      changeJobUniqueTo: null
    );

    result.ShouldBe(details);
    jobAndQueue.Received(1).InvalidatePalletCycle(17, 1, "operator", "new-casting", null);
  }

  [Test]
  public async Task BadRequestExceptionsBecomeHttp400()
  {
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    jobAndQueue
      .When(x => x.CancelLoad(17, "operation-1", "operator", "reason"))
      .Do(_ => throw new BadRequestException("malformed cancellation"));
    var controller = new JobsController(null!, jobAndQueue);

    var context = await InvokeThroughMiddleware(() =>
    {
      controller.CancelLoad(
        17,
        new CancelLoadRequest() { ExpectedLoadCancellationId = "operation-1", Reason = "reason" },
        "operator"
      );
      return Task.CompletedTask;
    });

    context.Response.StatusCode.ShouldBe(StatusCodes.Status400BadRequest);
  }

  [Test]
  public async Task ConflictExceptionsBecomeHttp409()
  {
    var jobAndQueue = Substitute.For<IJobAndQueueControl>();
    jobAndQueue
      .InvalidatePalletCycle(17, 2, "operator", null, null)
      .Returns(_ => throw new ConflictRequestException("stale proposal"));
    var controller = new JobsController(null!, jobAndQueue);

    var context = await InvokeThroughMiddleware(() =>
    {
      controller.InvalidatePalletCycle(
        materialId: 17,
        process: 2,
        operName: "operator",
        changeCastingTo: null,
        changeJobUniqueTo: null
      );
      return Task.CompletedTask;
    });

    context.Response.StatusCode.ShouldBe(StatusCodes.Status409Conflict);
  }

  [Test]
  public async Task BasketCompletionValidatesTheHttpBodyAndForwardsOnlyWorkId()
  {
    using var repository = RepositoryConfig.InitializeMemoryDB(null);
    var control = Substitute.For<IJobAndQueueControl>();
    control
      .When(c => c.CompleteBasketLoadStation("stale"))
      .Do(_ => throw new ConflictRequestException("Work changed."));
    var builder = WebApplication.CreateBuilder();
    builder.WebHost.UseKestrel().UseUrls("http://127.0.0.1:0");
    builder.Services.AddSingleton(repository);
    builder.Services.AddSingleton(control);
    builder
      .Services.AddControllers()
      .AddApplicationPart(typeof(JobsController).Assembly)
      .AddJsonOptions(options => FMSInsightWebHost.JsonSettings(options.JsonSerializerOptions));
    await using var app = builder.Build();
    app.UseMiddleware<ErrorHandlingMiddleware>();
    app.MapControllers();
    await app.StartAsync();
    using var http = new HttpClient
    {
      BaseAddress = new Uri(
        app.Services.GetRequiredService<IServer>()
          .Features.Get<IServerAddressesFeature>()!
          .Addresses.Single()
      ),
    };
    foreach (
      var body in new[]
      {
        "{}",
        "null",
        "{",
        "{\"WorkId\":null}",
        "{\"WorkId\":\" \"}",
        "{\"WorkId\":17}",
      }
    )
    {
      using var response = await http.PostAsync(
        "/api/v1/jobs/basket-load-station/complete",
        new StringContent(body, Encoding.UTF8, "application/json")
      );
      await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
    }
    control.DidNotReceiveWithAnyArgs().CompleteBasketLoadStation(default!);
    using var accepted = await http.PostAsync(
      "/api/v1/jobs/basket-load-station/complete",
      new StringContent("{\"WorkId\":\"current\"}", Encoding.UTF8, "application/json")
    );
    await Assert.That(accepted.StatusCode).IsEqualTo(HttpStatusCode.OK);
    control.Received(1).CompleteBasketLoadStation("current");
    using var conflict = await http.PostAsync(
      "/api/v1/jobs/basket-load-station/complete",
      new StringContent("{\"WorkId\":\"stale\"}", Encoding.UTF8, "application/json")
    );
    await Assert.That(conflict.StatusCode).IsEqualTo(HttpStatusCode.Conflict);
    await app.StopAsync();
  }

  private static async Task<HttpContext> InvokeThroughMiddleware(Func<Task> request)
  {
    var context = new DefaultHttpContext();
    context.Response.Body = new MemoryStream();
    var middleware = new ErrorHandlingMiddleware(_ => request());

    await middleware.Invoke(context);

    return context;
  }
}
