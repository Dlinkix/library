using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "FightMapConfig", menuName = "Game/Fight Map Config")]
public class FightMapConfig : ScriptableObject
{
    [System.Serializable]
    public class LaneRoomCounts
    {
        [Min(0)] public int mob = 5;
        [Min(0)] public int shop = 1;
        [Min(0)] public int randomEvent = 1;
        [Min(0)] public int anomaly = 1;
        [Min(0)] public int eliteMob = 1;
        [Min(0)] public int chest = 1;

        public int TotalCount =>
            mob +
            shop +
            randomEvent +
            anomaly +
            eliteMob +
            chest;

        public List<MapRoomType> BuildPool()
        {
            List<MapRoomType> rooms = new List<MapRoomType>(TotalCount);
            AddRepeatedRooms(rooms, MapRoomType.Mob, mob);
            AddRepeatedRooms(rooms, MapRoomType.Shop, shop);
            AddRepeatedRooms(rooms, MapRoomType.RandomEvent, randomEvent);
            AddRepeatedRooms(rooms, MapRoomType.Anomaly, anomaly);
            AddRepeatedRooms(rooms, MapRoomType.EliteMob, eliteMob);
            AddRepeatedRooms(rooms, MapRoomType.Chest, chest);
            return rooms;
        }

        private static void AddRepeatedRooms(List<MapRoomType> rooms, MapRoomType roomType, int count)
        {
            for (int i = 0; i < count; i++)
            {
                rooms.Add(roomType);
            }
        }
    }

    [Min(1)]
    [SerializeField] private int roomsPerLane = 10;

    [Header("Lane Counts")]
    [SerializeField] private LaneRoomCounts leftLane = new LaneRoomCounts();
    [SerializeField] private LaneRoomCounts middleLane = new LaneRoomCounts();
    [SerializeField] private LaneRoomCounts rightLane = new LaneRoomCounts();

    public int RoomsPerLane => roomsPerLane;

    public bool TryBuildLanePool(int laneIndex, out List<MapRoomType> rooms, out string error)
    {
        LaneRoomCounts laneCounts = GetLaneCounts(laneIndex);
        if (laneCounts == null)
        {
            rooms = null;
            error = $"Lane index {laneIndex} is out of range.";
            return false;
        }

        if (laneCounts.TotalCount != roomsPerLane)
        {
            rooms = null;
            error = $"Lane {laneIndex + 1} has {laneCounts.TotalCount} rooms configured, but Rooms Per Lane is {roomsPerLane}.";
            return false;
        }

        rooms = laneCounts.BuildPool();
        error = string.Empty;
        return true;
    }

    private LaneRoomCounts GetLaneCounts(int laneIndex)
    {
        switch (laneIndex)
        {
            case 0:
                return leftLane;
            case 1:
                return middleLane;
            case 2:
                return rightLane;
            default:
                return null;
        }
    }

    private void OnValidate()
    {
        roomsPerLane = Mathf.Max(1, roomsPerLane);
    }
}
