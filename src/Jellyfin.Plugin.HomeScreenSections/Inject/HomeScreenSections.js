'use strict';

if (typeof HomeScreenSectionsHandler == 'undefined') {
    const HomeScreenSectionsHandler = {
        init: function() {
            var MutationObserver = window.MutationObserver || window.WebKitMutationObserver;
            var myObserver = new MutationObserver(this.mutationHandler);
            var observerConfig = {childList: true, characterData: true, attributes: true, subtree: true};

            $("body").each(function () {
                myObserver.observe(this, observerConfig);
            });
        },
        mutationHandler: function (mutationRecords) {
            mutationRecords.forEach(function (mutation) {
                if (mutation.addedNodes && mutation.addedNodes.length > 0) {
                    [].some.call(mutation.addedNodes, function (addedNode) {
                        if ($(addedNode).hasClass('discover-card')) {
                            $(addedNode).on('click', '.discover-requestbutton', HomeScreenSectionsHandler.clickHandler);
                        }
                    });
                }
            });
        },
        clickHandler: function(event) {
            window.ApiClient.ajax({
                url: window.ApiClient.getUrl("HomeScreen/DiscoverRequest"),
                type: "POST",
                data: JSON.stringify({
                    UserId: window.ApiClient._currentUser.Id,
                    MediaType: $(this).data('media-type'),
                    MediaId: $(this).data('id'),
                }),
                contentType: 'application/json; charset=utf-8',
                dataType: 'json'
            }).then(function(response) {
                if (response.errors && response.errors.length > 0) {
                    Dashboard.alert("Item request failed. Check browser logs for details.");
                    console.error("Item request failed. Response including errors:");
                    console.error(response);
                } else {
                    Dashboard.alert("Item successfully requested");
                }
            }, function(error) {
                Dashboard.alert("Item request failed");
            })
        }
    };
    
    $(document).ready(function () {
        setTimeout(function () {
            HomeScreenSectionsHandler.init();
        }, 50);
    });
}

if (typeof TopTenSectionHandler == 'undefined') {
    const TopTenSectionHandler = {
        init: function () {
            var MutationObserver = window.MutationObserver || window.WebKitMutationObserver;
            var myObserver = new MutationObserver(this.mutationHandler);
            var observerConfig = {childList: true, characterData: true, attributes: true, subtree: true};

            $("body").each(function () {
                myObserver.observe(this, observerConfig);
            });
        },
        mutationHandler: function (mutationRecords) {
            mutationRecords.forEach(function (mutation) {
                if (mutation.addedNodes && mutation.addedNodes.length > 0) {
                    [].some.call(mutation.addedNodes, function (addedNode) {
                        if ($(addedNode).hasClass('card')) {
                            if ($(addedNode).parents('.top-ten').length > 0) {
                                var index = parseInt($(addedNode).attr('data-index'));
                                $(addedNode).attr('data-number', index + 1);
                            }
                        }
                    });
                }
            });
        }
    }

    setTimeout(function () {
        TopTenSectionHandler.init();
    }, 50);
}

if (typeof JellyseerrIntegrationHandler == 'undefined') {
    const JellyseerrIntegrationHandler = {
        config: null,
        init: function() {
            this.loadConfig().then(() => {
                if (this.config && this.config.JellyseerrExternalUrl) {
                    var MutationObserver = window.MutationObserver || window.WebKitMutationObserver;
                    var myObserver = new MutationObserver(this.mutationHandler.bind(this));
                    var observerConfig = {childList: true, subtree: true};
                    
                    $("body").each(function () {
                        myObserver.observe(this, observerConfig);
                    });
                    
                    this.injectButton();
                }
            });
        },
        loadConfig: function() {
            if (!window.ApiClient) return Promise.reject("ApiClient not found");
            var userId = window.ApiClient.getCurrentUserId();
            return window.ApiClient.getJSON(window.ApiClient.getUrl("ModularHomeViews/UserSettings?userId=" + userId))
                .then((config) => {
                    this.config = config;
                }).catch((err) => {
                    console.error("Failed to load Jellyseerr config", err);
                });
        },
        mutationHandler: function(mutationRecords) {
            this.injectButton();
        },
        injectButton: function() {
            var tabs = document.querySelector('.headerTabs .emby-tabs-slider');
            // If .headerTabs .emby-tabs-slider not found, try just .emby-tabs-slider (mobile view sometimes different)
            if (!tabs) tabs = document.querySelector('.emby-tabs-slider');

            if (tabs && !document.getElementById('jellyseerr-tab-btn')) {
                // Verify we are on home screen by checking URL or presence of home tabs
                // Usually Home/Favorites are present
                var hasHome = Array.from(tabs.querySelectorAll('button, a')).some(el => el.innerText.includes('Home') || el.innerText.includes('Accueil'));
                
                if (!hasHome && window.location.href.indexOf('home') === -1) return;

                var btn = document.createElement('button');
                btn.id = 'jellyseerr-tab-btn';
                btn.type = 'button';
                btn.className = 'emby-tab-button emby-button';
                btn.innerText = 'Jellyseerr';
                btn.style.marginLeft = '10px';
                btn.onclick = this.openJellyseerr.bind(this);
                
                tabs.appendChild(btn);
            }
        },
        openJellyseerr: function() {
            var dlg = document.createElement('div');
            dlg.className = 'dialogContainer';
            dlg.style.zIndex = 2000;
            dlg.style.position = 'fixed';
            dlg.style.top = 0;
            dlg.style.left = 0;
            dlg.style.width = '100%';
            dlg.style.height = '100%';
            dlg.style.display = 'flex';
            dlg.style.alignItems = 'center';
            dlg.style.justifyContent = 'center';
            
            var mask = document.createElement('div');
            mask.className = 'dialogMask';
            mask.style.position = 'absolute';
            mask.style.top = 0;
            mask.style.left = 0;
            mask.style.width = '100%';
            mask.style.height = '100%';
            mask.style.backgroundColor = 'rgba(0,0,0,0.7)';
            mask.onclick = () => document.body.removeChild(dlg);
            
            var content = document.createElement('div');
            content.className = 'dialogContent';
            content.style.position = 'relative';
            content.style.width = '95%';
            content.style.height = '95%';
            content.style.backgroundColor = '#101010';
            content.style.borderRadius = '10px';
            content.style.overflow = 'hidden';
            content.style.zIndex = 2001;
            
            var iframe = document.createElement('iframe');
            iframe.src = this.config.JellyseerrExternalUrl;
            iframe.style.width = '100%';
            iframe.style.height = '100%';
            iframe.style.border = 'none';
            
            var closeBtn = document.createElement('button');
            closeBtn.className = 'paper-icon-button-light';
            closeBtn.style.position = 'absolute';
            closeBtn.style.top = '10px';
            closeBtn.style.right = '10px';
            closeBtn.style.zIndex = 2002;
            closeBtn.style.backgroundColor = 'rgba(0,0,0,0.5)';
            closeBtn.style.borderRadius = '50%';
            closeBtn.innerHTML = '<span class="material-icons close" style="color:white; font-size: 24px;">x</span>';
            closeBtn.onclick = () => document.body.removeChild(dlg);
            
            content.appendChild(iframe);
            content.appendChild(closeBtn);
            dlg.appendChild(mask);
            dlg.appendChild(content);
            
            document.body.appendChild(dlg);
        }
    };
    
    $(document).ready(function () {
        setTimeout(function () {
            JellyseerrIntegrationHandler.init();
        }, 1000);
    });
}