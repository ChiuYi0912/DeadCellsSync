using System.Collections.Generic;
using NetcodeArena.Core.Networking;
using Xunit;

namespace NetcodeArena.Core.Tests;

public class SimulatedNetworkChannelTests
{
    [Fact]
    public void Send_WithFixedLatency_DeliversExactlyAfterThatManyTicks()
    {
        var channel = new SimulatedNetworkChannel<int>(seed: 1, packetLossProbability: 0, minLatencyTicks: 3, maxLatencyTicks: 3);

        channel.Send(42, currentTick: 10);

        Assert.Empty(channel.ReceiveUpTo(12));
        var delivered = channel.ReceiveUpTo(13);

        Assert.Single(delivered);
        Assert.Equal(42, delivered[0]);
    }

    [Fact]
    public void Send_WithZeroPacketLoss_NeverDropsMessages()
    {
        var channel = new SimulatedNetworkChannel<int>(seed: 2, packetLossProbability: 0.0, minLatencyTicks: 0, maxLatencyTicks: 5);

        for (var i = 0; i < 500; i++)
        {
            channel.Send(i, (uint)i);
        }

        var delivered = channel.ReceiveUpTo(10_000);

        Assert.Equal(500, delivered.Count);
        Assert.Equal(0, channel.Dropped);
    }

    [Fact]
    public void Send_WithFullPacketLoss_DropsEverything()
    {
        var channel = new SimulatedNetworkChannel<int>(seed: 3, packetLossProbability: 1.0, minLatencyTicks: 0, maxLatencyTicks: 0);

        for (var i = 0; i < 100; i++)
        {
            channel.Send(i, (uint)i);
        }

        var delivered = channel.ReceiveUpTo(1000);

        Assert.Empty(delivered);
        Assert.Equal(100, channel.Dropped);
    }

    [Fact]
    public void Send_WithSameSeed_ProducesIdenticalDeliveryPattern()
    {
        var channelOne = new SimulatedNetworkChannel<int>(seed: 99, packetLossProbability: 0.3, minLatencyTicks: 1, maxLatencyTicks: 6);
        var channelTwo = new SimulatedNetworkChannel<int>(seed: 99, packetLossProbability: 0.3, minLatencyTicks: 1, maxLatencyTicks: 6);

        for (var i = 0; i < 200; i++)
        {
            channelOne.Send(i, (uint)i);
            channelTwo.Send(i, (uint)i);
        }

        var deliveredOne = channelOne.ReceiveUpTo(1000);
        var deliveredTwo = channelTwo.ReceiveUpTo(1000);

        Assert.Equal(deliveredOne, deliveredTwo);
        Assert.Equal(channelOne.Dropped, channelTwo.Dropped);
    }

    [Fact]
    public void Send_ApproximatePacketLossRate_MatchesConfiguredProbabilityOverManySamples()
    {
        var channel = new SimulatedNetworkChannel<int>(seed: 7, packetLossProbability: 0.5, minLatencyTicks: 0, maxLatencyTicks: 0);

        const int sampleSize = 5000;
        for (var i = 0; i < sampleSize; i++)
        {
            channel.Send(i, 0);
        }

        var dropRate = channel.Dropped / (double)sampleSize;
        Assert.InRange(dropRate, 0.45, 0.55);
    }

    [Fact]
    public void Jitter_CanCauseOutOfOrderDelivery_WhichReceiveUpToSortsByDeliveryTick()
    {
        var channel = new SimulatedNetworkChannel<int>(seed: 123, packetLossProbability: 0, minLatencyTicks: 0, maxLatencyTicks: 20);

        for (var i = 0; i < 50; i++)
        {
            channel.Send(i, 0);
        }

        var delivered = channel.ReceiveUpTo(1000);

        Assert.Equal(50, delivered.Count);
        // We cannot assert a specific order (depends on RNG), but every value must have
        // arrived exactly once - i.e. no message is lost or duplicated by the jitter path.
        var sortedCopy = new List<int>(delivered);
        sortedCopy.Sort();
        for (var i = 0; i < 50; i++)
        {
            Assert.Equal(i, sortedCopy[i]);
        }
    }
}
