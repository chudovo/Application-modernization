// An AngularJS 1.8 application as they were written: controllers, templates with filters, ngRoute, $http.
angular.module('orders', ['ngRoute', 'ngSanitize'])
  .config(function ($routeProvider) {
    $routeProvider
      .when('/orders', { templateUrl: 'list.html', controller: 'ListController', controllerAs: 'vm' })
      .when('/orders/:id', { templateUrl: 'detail.html', controller: 'DetailController', controllerAs: 'vm' })
      .otherwise('/orders');
  })
  .controller('ListController', function ($http) {
    var vm = this;
    vm.orders = [];
    vm.search = '';
    vm.status = '';
    vm.sort = 'placed';
    vm.reverse = true;

    vm.load = function () {
      // An empty status is passed as null. $http leaves null and undefined parameters out of the address.
      $http.get('api/orders', { params: { status: vm.status || null } }).then(function (response) {
        vm.orders = response.data;
      });
    };

    vm.sortBy = function (column) {
      vm.reverse = vm.sort === column ? !vm.reverse : false;
      vm.sort = column;
    };

    vm.load();
  })
  .controller('DetailController', function ($http, $routeParams) {
    var vm = this;

    $http.get('api/orders/' + $routeParams.id).then(
      function (response) { vm.order = response.data; },
      function () { vm.notFound = true; });

    vm.save = function () {
      vm.saved = false;
      $http.put('api/orders/' + vm.order.id, { status: vm.order.status, discount: vm.order.discount }).then(function () {
        vm.saved = true;
      });
    };
  });
