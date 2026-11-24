namespace HelixReborn.Api.Tests;

using System.Net;
using System.Net.Http.Json;
using Fixtures;
using Microsoft.AspNetCore.Mvc;
using Models;
using Xunit;

public class ExperimentsControllerTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
  private const string Route = "/api/experiments";

  private readonly HttpClient _user = factory.CreateUserClient();

  [Fact]
  public async Task Create_ThenList_ThenGet_RoundTripsExperiment()
  {
    var response = await CreateResponse(_user, "Bone density", "A description");
    response.EnsureSuccessStatusCode();
    var created = (await response.Content.ReadFromJsonAsync<ExperimentResponse>())!;

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Equal($"api/experiments/{created.Id}", response.Headers.Location?.ToString());
    Assert.Equal("Bone density", created.Name);
    Assert.Equal("A description", created.Description);
    Assert.Equal("user-1", created.OwnerSub);
    Assert.Null(created.Dataset);

    var listed = await _user.GetFromJsonAsync<List<ExperimentResponse>>(Route);
    Assert.Contains(listed!, x => x.Id == created.Id);

    var fetched = await _user.GetFromJsonAsync<ExperimentResponse>($"{Route}/{created.Id}");
    Assert.Equal(created.Id, fetched!.Id);
    Assert.Equal("Bone density", fetched.Name);
  }

  [Fact]
  public async Task List_ReturnsNewestFirst()
  {
    var first = await Create(_user, "First", null);
    var second = await Create(_user, "Second", null);

    var listed = await _user.GetFromJsonAsync<List<ExperimentResponse>>(Route);

    var firstIndex = listed!.FindIndex(x => x.Id == first.Id);
    var secondIndex = listed.FindIndex(x => x.Id == second.Id);
    Assert.True(secondIndex < firstIndex);
  }

  [Fact]
  public async Task List_ExcludesOtherUsersExperiments_ByDefault()
  {
    var other = factory.CreateUserClient("user-2");
    var theirs = await Create(other, "Not mine", null);

    var listed = await _user.GetFromJsonAsync<List<ExperimentResponse>>(Route);

    Assert.DoesNotContain(listed!, x => x.Id == theirs.Id);
  }

  [Fact]
  public async Task List_ReturnsOnlyCallersExperiments_WhenAdminOmitsAll()
  {
    var theirs = await Create(_user, "User owned", null);
    var admin = factory.CreateAdminClient();

    var listed = await admin.GetFromJsonAsync<List<ExperimentResponse>>(Route);

    Assert.DoesNotContain(listed!, x => x.Id == theirs.Id);
  }

  [Fact]
  public async Task List_ReturnsEveryUsersExperiments_WhenAdminPassesAllTrue()
  {
    var theirs = await Create(_user, "Visible to admin", null);
    var admin = factory.CreateAdminClient();

    var listed = await admin.GetFromJsonAsync<List<ExperimentResponse>>($"{Route}?all=true");

    Assert.Contains(listed!, x => x.Id == theirs.Id);
  }

  [Fact]
  public async Task Get_ReturnsNotFound_ForAnotherUsersExperiment()
  {
    var other = factory.CreateUserClient("user-3");
    var theirs = await Create(other, "Private", null);

    var response = await _user.GetAsync($"{Route}/{theirs.Id}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Get_ReturnsNotFound_WhenExperimentDoesNotExist()
  {
    var response = await _user.GetAsync($"{Route}/{Guid.NewGuid()}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Get_ReturnsExperiment_ForAdminReadingAnotherUsersExperiment()
  {
    var other = factory.CreateUserClient("user-4");
    var theirs = await Create(other, "Admin can read", null);
    var admin = factory.CreateAdminClient();

    var response = await admin.GetAsync($"{Route}/{theirs.Id}");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task Update_ReturnsNotFound_ForAdminUpdatingAnotherUsersExperiment()
  {
    var other = factory.CreateUserClient("user-5");
    var theirs = await Create(other, "Admin cannot write", null);
    var admin = factory.CreateAdminClient();

    var response = await admin.PatchAsJsonAsync($"{Route}/{theirs.Id}", new { name = "Hijacked" });

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Update_ChangesNameAndDescription_ForOwner()
  {
    var experiment = await Create(_user, "Original", "Original description");

    var response = await _user.PatchAsJsonAsync(
      $"{Route}/{experiment.Id}",
      new { name = "Updated", description = "Updated description" });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var updated = await response.Content.ReadFromJsonAsync<ExperimentResponse>();
    Assert.Equal("Updated", updated!.Name);
    Assert.Equal("Updated description", updated.Description);
    Assert.True(updated.UpdatedAt >= experiment.UpdatedAt);
  }

  [Fact]
  public async Task Delete_ReturnsNotFound_ForAdminDeletingAnotherUsersExperiment()
  {
    var other = factory.CreateUserClient("user-6");
    var theirs = await Create(other, "Admin cannot delete", null);
    var admin = factory.CreateAdminClient();

    var response = await admin.DeleteAsync($"{Route}/{theirs.Id}");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task Delete_RemovesExperimentAndEveryStoredObjectUnderIt()
  {
    var experiment = await Create(_user, "To delete", null);
    await factory.ObjectStorage.PutAsync(
      $"experiments/{experiment.Id:D}/datasets/{Guid.NewGuid():D}/data.csv",
      new MemoryStream([1, 2, 3]),
      "text/csv",
      CancellationToken.None);

    var response = await _user.DeleteAsync($"{Route}/{experiment.Id}");

    Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    var getResponse = await _user.GetAsync($"{Route}/{experiment.Id}");
    Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    var remaining = await factory.ObjectStorage.ListAsync($"experiments/{experiment.Id:D}/", CancellationToken.None);
    Assert.Empty(remaining);
  }

  [Theory]
  [InlineData("")]
  public async Task Create_ReturnsBadRequest_WhenNameIsEmpty(string name)
  {
    var response = await _user.PostAsJsonAsync(Route, new { name });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task Create_ReturnsBadRequest_WhenNameIsTooLong()
  {
    var response = await _user.PostAsJsonAsync(Route, new { name = new string('a', 101) });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  private static async Task<ExperimentResponse> Create(HttpClient client, string name, string? description)
  {
    var response = await CreateResponse(client, name, description);
    response.EnsureSuccessStatusCode();
    return (await response.Content.ReadFromJsonAsync<ExperimentResponse>())!;
  }

  private static Task<HttpResponseMessage> CreateResponse(HttpClient client, string name, string? description) =>
    client.PostAsJsonAsync(Route, new { name, description });
}
