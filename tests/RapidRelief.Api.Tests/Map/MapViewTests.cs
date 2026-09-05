using RapidRelief.Client.Common.Map;
using RapidRelief.Shared.Contracts.Common;
using RapidRelief.Shared.Contracts.Enums;
using RapidRelief.Shared.Contracts.ReadModels;

namespace RapidRelief.Api.Tests.Map;

/// <summary>
/// The shared map service. Before it existed every page built its own marker list, so the same
/// incident could be a different colour, carry a different label and use a colliding id from one
/// screen to the next. These tests pin the rules that replaced that.
/// </summary>
public sealed class MapViewTests
{
    private static readonly GeoPoint Dhaka = new(23.8103, 90.4125);

    private static MapPlacemark At(string key, double lat, double lng, bool critical = false, double weight = 1) =>
        new(key, new GeoPoint(lat, lng), key, IsCritical: critical, Weight: weight);

    [Fact]
    public void An_empty_view_centres_on_the_configured_fallback_rather_than_null_island()
    {
        var view = new MapView(Dhaka);

        Assert.Equal(Dhaka, view.Center);
        Assert.Empty(view.Markers);
    }

    [Fact]
    public void Two_layers_can_hold_the_same_entity_id_without_colliding_on_the_map()
    {
        var shared = Guid.NewGuid().ToString("N");
        var view = new MapView(Dhaka);
        view.SetLayer(MapLayerId.Incidents, [At(shared, 23.81, 90.41)]);
        view.SetLayer(MapLayerId.Teams, [At(shared, 23.82, 90.42)]);

        Assert.Equal(2, view.Markers.Select(m => m.Id).Distinct().Count());
    }

    [Fact]
    public void Hiding_a_layer_removes_its_markers_but_keeps_its_count_for_the_legend()
    {
        var view = new MapView(Dhaka);
        view.SetLayer(MapLayerId.Shelters, [At("a", 23.81, 90.41), At("b", 23.82, 90.42)]);

        view.SetVisible(MapLayerId.Shelters, false);

        Assert.Empty(view.Markers);
        Assert.Equal(2, view.CountIn(MapLayerId.Shelters));
        Assert.Equal(0, view.VisibleCountIn(MapLayerId.Shelters));
        Assert.False(Assert.Single(view.Legend).Visible);
    }

    [Fact]
    public void Critical_only_keeps_the_sos_pins_and_drops_the_rest()
    {
        var view = new MapView(Dhaka);
        view.SetLayer(MapLayerId.Incidents, [At("routine", 23.81, 90.41), At("sos", 23.82, 90.42, critical: true)]);

        view.CriticalOnly = true;

        Assert.Equal(MapView.MarkerId(MapLayerId.Incidents, "sos"), Assert.Single(view.Markers).Id);
    }

    [Fact]
    public void A_critical_incident_is_styled_as_sos_wherever_it_is_rendered()
    {
        var view = new MapView(Dhaka);
        view.SetLayer(MapLayerId.Incidents, [At("sos", 23.81, 90.41, critical: true), At("plain", 23.82, 90.42)]);

        Assert.Equal(MapMarkerKind.Sos, view.Markers.Single(m => m.Id.EndsWith("sos", StringComparison.Ordinal)).Kind);
        Assert.Equal(MapMarkerKind.Incident, view.Markers.Single(m => m.Id.EndsWith("plain", StringComparison.Ordinal)).Kind);
    }

    [Fact]
    public void The_radius_filter_only_applies_once_we_know_where_the_viewer_is()
    {
        var view = new MapView(Dhaka);
        // ~11 km north of the centre.
        view.SetLayer(MapLayerId.Shelters, [At("near", 23.8110, 90.4125), At("far", 23.9103, 90.4125)]);
        view.RadiusKm = 5;

        Assert.Equal(2, view.Markers.Count);

        view.UserLocation = Dhaka;

        Assert.EndsWith("near", Assert.Single(view.Markers).Id, StringComparison.Ordinal);
    }

    [Fact]
    public void Visible_items_come_back_nearest_first_once_the_viewer_is_located()
    {
        var view = new MapView(Dhaka);
        view.SetLayer(MapLayerId.Shelters, [At("far", 23.9103, 90.4125), At("near", 23.8110, 90.4125)]);
        view.UserLocation = Dhaka;

        Assert.Equal(["near", "far"], view.VisibleItems.Select(x => x.Placemark.Key));
    }

    [Fact]
    public void Search_matches_the_title_the_detail_and_the_status()
    {
        var view = new MapView(Dhaka);
        view.SetLayer(MapLayerId.Shelters,
        [
            new MapPlacemark("a", Dhaka, "Mirpur school", Detail: "40 free", Status: "Open"),
            new MapPlacemark("b", Dhaka, "Uttara hall", Detail: "0 free", Status: "Closed"),
        ]);

        view.Search = "closed";

        Assert.EndsWith("b", Assert.Single(view.Markers).Id, StringComparison.Ordinal);
    }

    [Fact]
    public void The_heat_layer_is_empty_until_it_is_asked_for_and_then_follows_the_filters()
    {
        var view = new MapView(Dhaka);
        view.SetLayer(MapLayerId.Incidents,
            [At("a", 23.81, 90.41, critical: true, weight: 4), At("b", 23.82, 90.42, weight: 1)]);

        Assert.Empty(view.HeatPoints);

        view.ShowHeatmap = true;
        Assert.Equal(2, view.HeatPoints.Count);

        view.CriticalOnly = true;
        Assert.Equal(4, Assert.Single(view.HeatPoints).Weight);
    }

    [Fact]
    public void Distance_and_bearing_are_unknown_until_the_viewer_is_located()
    {
        var view = new MapView(Dhaka);
        var target = At("t", 23.9103, 90.4125);

        Assert.Null(view.DistanceKmTo(target));
        Assert.Equal("—", view.DistanceTextTo(target));
        Assert.Null(view.BearingTo(target));

        view.UserLocation = Dhaka;

        Assert.InRange(view.DistanceKmTo(target)!.Value, 10, 12);
        Assert.Equal("north", view.BearingTo(target));
    }

    [Fact]
    public void A_marker_tooltip_carries_the_distance_so_a_responder_does_not_have_to_measure_it()
    {
        var view = new MapView(Dhaka) { UserLocation = Dhaka };
        view.SetLayer(MapLayerId.Incidents, [new MapPlacemark("a", Dhaka, "Collapse", Detail: "SOS")]);

        var marker = Assert.Single(view.Markers);
        Assert.Contains("Collapse — SOS", marker.Title, StringComparison.Ordinal);
        Assert.Contains("away", marker.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Directions_hand_off_includes_the_origin_when_the_viewer_is_located()
    {
        var view = new MapView(Dhaka);
        var target = At("t", 23.9103, 90.4125);

        Assert.DoesNotContain("origin=", view.DirectionsUrlTo(target), StringComparison.Ordinal);

        view.UserLocation = Dhaka;
        Assert.Contains("origin=23.810300,90.412500", view.DirectionsUrlTo(target), StringComparison.Ordinal);
    }

    [Fact]
    public void Every_state_change_that_alters_the_render_raises_changed_once()
    {
        var view = new MapView(Dhaka);
        var changes = 0;
        view.Changed += () => changes++;

        view.SetLayer(MapLayerId.Incidents, [At("a", 23.81, 90.41)]);
        view.CriticalOnly = true;
        view.CriticalOnly = true; // no-op: same value
        view.SetVisible(MapLayerId.Incidents, false);
        view.SetVisible(MapLayerId.Incidents, false); // no-op

        Assert.Equal(3, changes);
    }

    [Fact]
    public void An_sos_report_is_hot_on_the_heat_layer_before_ai_triage_has_scored_it()
    {
        var untriaged = SharedMapAdapters.Weight(isSos: true, Severity.Severe, priorityScore: null);
        var routine = SharedMapAdapters.Weight(isSos: false, Severity.Minor, priorityScore: null);

        Assert.True(untriaged > routine);
    }

    [Fact]
    public void A_shelter_with_no_space_left_is_flagged_critical()
    {
        var full = new ShelterSummaryDto(Guid.NewGuid(), "Full", Dhaka, 100, 100, IsOpen: true).ToPlacemark();
        var open = new ShelterSummaryDto(Guid.NewGuid(), "Open", Dhaka, 100, 10, IsOpen: true).ToPlacemark();

        Assert.True(full.IsCritical);
        Assert.False(open.IsCritical);
        Assert.Equal("90 of 100 free", open.Detail);
    }
}
