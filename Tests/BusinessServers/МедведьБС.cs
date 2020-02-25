﻿//------------------------------------------------------------------------------
// <auto-generated>
//     Этот код создан программой.
//     Исполняемая версия:4.0.30319.42000
//
//     Изменения в этом файле могут привести к неправильной работе и будут потеряны в случае
//     повторной генерации кода.
// </auto-generated>
//------------------------------------------------------------------------------

namespace NewPlatform.Flexberry.ORM.ODataService.Tests
{
    using System;
    using System.Xml;


    // *** Start programmer edit section *** (Using statements)
    using System.Collections.Generic;
    using System.Linq;
    using ICSSoft.STORMNET;

    // *** End programmer edit section *** (Using statements)


    /// <summary>
    /// МедведьБС.
    /// </summary>
    // *** Start programmer edit section *** (МедведьБС CustomAttributes)

    // *** End programmer edit section *** (МедведьБС CustomAttributes)
    [ICSSoft.STORMNET.AccessType(ICSSoft.STORMNET.AccessType.none)]
    public class МедведьБС : ICSSoft.STORMNET.Business.BusinessServer
    {
        
        // *** Start programmer edit section *** (МедведьБС CustomMembers)

        // *** End programmer edit section *** (МедведьБС CustomMembers)

        
        // *** Start programmer edit section *** (OnUpdateМедведь CustomAttributes)

        // *** End programmer edit section *** (OnUpdateМедведь CustomAttributes)
        public virtual ICSSoft.STORMNET.DataObject[] OnUpdateМедведь(NewPlatform.Flexberry.ORM.ODataService.Tests.Медведь UpdatedObject)
        {
            // *** Start programmer edit section *** (OnUpdateМедведь)
            var updatedObjects = new List<DataObject>();
            if (UpdatedObject.GetStatus() == ObjectStatus.Created)
            {
                UpdatedObject.ПолеБС = "Object created.";
            }
            else if (UpdatedObject.GetStatus() == ObjectStatus.Altered)
            {
                if (UpdatedObject.IsAlteredProperty(x => x.ЛесОбитания) && UpdatedObject.ЛесОбитания != null)
                {
                    UpdatedObject.ПолеБС = $"Медведь обитает в {UpdatedObject.ЛесОбитания.Название}";
                }

                IEnumerable<Берлога> берлоги = UpdatedObject.Берлога.GetAllObjects().Cast<Берлога>();

                var новаяБерлога = берлоги.FirstOrDefault(б => б.GetStatus() == ObjectStatus.Created);
                if (новаяБерлога != null)
                {
                    foreach (Берлога берлога in UpdatedObject.Берлога)
                    {
                        if (берлога != новаяБерлога)
                        {
                            берлога.Заброшена = true;
                            updatedObjects.Add(берлога);
                        }
                    }
                }

                var последняяБерлога = берлоги.FirstOrDefault(б => б.GetStatus() == ObjectStatus.Altered);
                if (последняяБерлога != null)
                {
                    foreach (Берлога берлога in UpdatedObject.Берлога)
                    {
                        if (берлога != последняяБерлога)
                        {
                            берлога.Заброшена = true;
                            updatedObjects.Add(берлога);
                        }
                    }

                    UpdatedObject.ЦветГлаз = последняяБерлога.Наименование;
                    последняяБерлога.Заброшена = false;
                    updatedObjects.Add(последняяБерлога);
                }

                var разрушеннаяБерлога = берлоги.FirstOrDefault(б => б.GetStatus() == ObjectStatus.Deleted);
                if (разрушеннаяБерлога != null)
                {
                    foreach (Берлога берлога in UpdatedObject.Берлога)
                    {
                        if (берлога != разрушеннаяБерлога)
                        {
                            берлога.Комфортность += 1;
                            updatedObjects.Add(берлога);
                        }
                    }
                }
            }

            return updatedObjects.Distinct().ToArray();
            // *** End programmer edit section *** (OnUpdateМедведь)
        }
    }
}
